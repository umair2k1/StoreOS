using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Identity;
using StoreOS.Application.Identity.Login;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Identity;

[Collection("postgres")]
public class RefreshTokenIssuanceTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private const string Password = "StrongPassword123!";

    public RefreshTokenIssuanceTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var passwordHash = _hasher.Hash(Password);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Refresh Token Tenant", subdomain: "rt-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var user = User.Create(_tenantId, "rt-user@test.com", passwordHash, "RT User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM refresh_tokens WHERE user_id = @uId; " +
            "DELETE FROM users WHERE id = @uId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);

        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Login_SetsStoreOsRefreshTokenCookie_WithCorrectAttributes()
    {
        var command = new LoginCommand("rt-user@test.com", Password, _tenantId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieHeader = cookies!.FirstOrDefault(c => c.StartsWith("storeos_refresh_token="));
        cookieHeader.Should().NotBeNull();

        var normalizedCookie = cookieHeader!.ToLowerInvariant();
        normalizedCookie.Should().Contain("httponly");
        normalizedCookie.Should().Contain("secure");
        normalizedCookie.Should().Contain("samesite=strict");
        normalizedCookie.Should().Contain("path=/api/auth");
    }

    [Fact]
    public async Task Login_ResponseBody_DoesNotLeakRawRefreshTokenOrRefreshField()
    {
        var command = new LoginCommand("rt-user@test.com", Password, _tenantId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieHeader = cookies!.First(c => c.StartsWith("storeos_refresh_token="));

        // Raw token value is between '=' and the first ';'
        var rawToken = cookieHeader.Split(';')[0].Substring("storeos_refresh_token=".Length);
        rawToken.Should().NotBeNullOrWhiteSpace();

        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotContain(rawToken);

        using var jsonDoc = JsonDocument.Parse(rawBody);
        foreach (var property in jsonDoc.RootElement.EnumerateObject())
        {
            property.Name.ToLowerInvariant().Should().NotContain("refresh");
        }
    }

    [Fact]
    public async Task Login_Database_StoresOnlyHashedRefreshToken()
    {
        var command = new LoginCommand("rt-user@test.com", Password, _tenantId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieHeader = cookies!.First(c => c.StartsWith("storeos_refresh_token="));
        var rawToken = cookieHeader.Split(';')[0].Substring("storeos_refresh_token=".Length);

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT token_hash, is_revoked, family_id, expires_at FROM refresh_tokens WHERE user_id = @uId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);

        await using var reader = await cmd.ExecuteReaderAsync();
        var rows = new List<(string tokenHash, bool isRevoked, Guid familyId, DateTimeOffset expiresAt)>();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetGuid(2), reader.GetFieldValue<DateTimeOffset>(3)));
        }

        rows.Should().HaveCount(1);
        var record = rows[0];

        record.isRevoked.Should().BeFalse();
        record.tokenHash.Should().NotBe(rawToken);
        record.tokenHash.Should().Be(RefreshTokenFormat.Hash(rawToken));
        record.expiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(30), TimeSpan.FromMinutes(5));
    }
}
