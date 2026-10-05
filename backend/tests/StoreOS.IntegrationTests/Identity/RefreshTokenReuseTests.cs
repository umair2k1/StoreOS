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
public class RefreshTokenReuseTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private const string Password = "StrongPassword123!";

    public RefreshTokenReuseTests(PostgresTestFixture fixture)
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

        var tenantA = Tenant.Create("Tenant A", subdomain: "rta-" + _tenantAId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenantA, _tenantAId);
        db.Tenants.Add(tenantA);

        var tenantB = Tenant.Create("Tenant B", subdomain: "rtb-" + _tenantBId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenantB, _tenantBId);
        db.Tenants.Add(tenantB);

        var user = User.Create(_tenantAId, "reuse-user@test.com", passwordHash, "Reuse User");
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
            "DELETE FROM tenants WHERE id IN (@tIdA, @tIdB);", conn);

        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("tIdA", _tenantAId);
        cmd.Parameters.AddWithValue("tIdB", _tenantBId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private static string ExtractRawRefreshToken(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        var cookieHeader = cookies!.First(c => c.StartsWith("storeos_refresh_token="));
        return cookieHeader.Split(';')[0].Substring("storeos_refresh_token=".Length);
    }

    private HttpRequestMessage CreateRefreshRequest(string rawToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", $"storeos_refresh_token={rawToken}");
        return request;
    }

    [Fact]
    public async Task NormalRotation_Succeeds_And_RotatesCookie_And_MarksPreviousRevoked()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand("reuse-user@test.com", Password, _tenantAId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token1 = ExtractRawRefreshToken(loginResponse);

        // 2. Refresh using token1
        var refreshResponse = await _client.SendAsync(CreateRefreshRequest(token1));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token2 = ExtractRawRefreshToken(refreshResponse);

        token2.Should().NotBe(token1);

        // 3. Verify in database
        var hash1 = RefreshTokenFormat.Hash(token1);
        var hash2 = RefreshTokenFormat.Hash(token2);

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT id, is_revoked, replaced_by_token_id FROM refresh_tokens WHERE token_hash = @h1;", conn);
        cmd.Parameters.AddWithValue("h1", hash1);

        await using var reader = await cmd.ExecuteReaderAsync();
        reader.Read().Should().BeTrue();
        var id1 = reader.GetGuid(0);
        var isRevoked1 = reader.GetBoolean(1);
        var replacedBy1 = reader.GetGuid(2);

        isRevoked1.Should().BeTrue();
        replacedBy1.Should().NotBeEmpty();
        await reader.CloseAsync();

        // Verify replacedBy matches token2's ID
        await using var cmd2 = new NpgsqlCommand(
            "SELECT id, is_revoked FROM refresh_tokens WHERE token_hash = @h2;", conn);
        cmd2.Parameters.AddWithValue("h2", hash2);

        await using var reader2 = await cmd2.ExecuteReaderAsync();
        reader2.Read().Should().BeTrue();
        var id2 = reader2.GetGuid(0);
        var isRevoked2 = reader2.GetBoolean(1);

        id2.Should().Be(replacedBy1);
        isRevoked2.Should().BeFalse();
    }

    [Fact]
    public async Task ReuseDetection_RevokesEntireFamily_BumpsTokenVersion_AndRejectsSubsequentRefresh()
    {
        // 1. Login -> token1
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand("reuse-user@test.com", Password, _tenantAId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token1 = ExtractRawRefreshToken(loginResponse);

        // 2. First legitimate refresh -> token2
        var refresh1Response = await _client.SendAsync(CreateRefreshRequest(token1));
        refresh1Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var token2 = ExtractRawRefreshToken(refresh1Response);

        // 3. THEFT/REPLAY: Stolen token1 is presented again!
        var replayResponse = await _client.SendAsync(CreateRefreshRequest(token1));
        replayResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var replayJson = await replayResponse.Content.ReadFromJsonAsync<JsonElement>();
        replayJson.GetProperty("code").GetString().Should().Be("refresh_token_reuse_detected");

        // 4. Verify DB: ALL tokens in the family are now revoked, and user token_version is incremented
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        var hash1 = RefreshTokenFormat.Hash(token1);
        var hash2 = RefreshTokenFormat.Hash(token2);

        await using (var cmd = new NpgsqlCommand(
            "SELECT is_revoked FROM refresh_tokens WHERE token_hash IN (@h1, @h2);", conn))
        {
            cmd.Parameters.AddWithValue("h1", hash1);
            cmd.Parameters.AddWithValue("h2", hash2);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                reader.GetBoolean(0).Should().BeTrue("every token in the family must be revoked on reuse");
            }
        }

        await using (var cmdUser = new NpgsqlCommand("SELECT token_version FROM users WHERE id = @uId;", conn))
        {
            cmdUser.Parameters.AddWithValue("uId", _userId);
            var tokenVersion = (int)(await cmdUser.ExecuteScalarAsync())!;
            tokenVersion.Should().Be(2, "token_version must be bumped on theft detection");
        }

        // 5. Confirm legitimate token2 can NO LONGER be used because its family was revoked
        var refresh2Response = await _client.SendAsync(CreateRefreshRequest(token2));
        refresh2Response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ExpiredRefreshToken_Returns401RefreshTokenExpired()
    {
        // 1. Login -> token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand("reuse-user@test.com", Password, _tenantAId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = ExtractRawRefreshToken(loginResponse);

        // 2. Set expires_at in DB to the past
        var hash = RefreshTokenFormat.Hash(token);
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "UPDATE refresh_tokens SET expires_at = now() - interval '1 day' WHERE token_hash = @h;", conn))
        {
            cmd.Parameters.AddWithValue("h", hash);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Attempt refresh
        var refreshResponse = await _client.SendAsync(CreateRefreshRequest(token));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("refresh_token_expired");
    }

    [Fact]
    public async Task CrossTenantTamperedToken_Returns401InvalidRefreshToken()
    {
        // 1. Login in Tenant A -> token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand("reuse-user@test.com", Password, _tenantAId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = ExtractRawRefreshToken(loginResponse);

        // 2. Tamper token prefix to claim Tenant B
        var parts = token.Split('.');
        var tamperedToken = $"{_tenantBId:N}.{parts[1]}";

        // 3. Attempt refresh: RLS scopes query to Tenant B, where this hash does not exist
        var refreshResponse = await _client.SendAsync(CreateRefreshRequest(tamperedToken));
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("invalid_refresh_token");
    }
}
