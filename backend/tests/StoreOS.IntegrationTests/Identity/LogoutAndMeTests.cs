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
public class LogoutAndMeTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private const string Password = "StrongPassword123!";

    public LogoutAndMeTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Logout Tenant", subdomain: "logout-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "B01", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "Operator");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var rolePerm = RolePermission.Create(_roleId, "sales:create");
        db.RolePermissions.Add(rolePerm);

        var user = User.Create(_tenantId, "logout-user@test.com", passwordHash, "Logout User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var userBranch = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(userBranch);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, @bId);", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM refresh_tokens WHERE user_id = @uId; " +
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);

        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
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

    [Fact]
    public async Task GetMe_WithoutToken_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
    [Fact]
    public async Task GetMe_WithValidToken_Returns200WithClaims()
    {
        // 1. Login to get valid access token
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand("logout-user@test.com", Password, _tenantId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginJson.GetProperty("accessToken").GetString();

        // 2. Call GET /api/auth/me with Bearer token
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var meJson = await response.Content.ReadFromJsonAsync<JsonElement>();
        meJson.GetProperty("userId").GetGuid().Should().Be(_userId);
        meJson.GetProperty("tenantId").GetGuid().Should().Be(_tenantId);
        meJson.GetProperty("allBranches").GetBoolean().Should().BeFalse();

        var branchIds = meJson.GetProperty("branchIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        branchIds.Should().Contain(_branchId);

        var perms = meJson.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToList();
        perms.Should().Contain("sales:create");

        meJson.GetProperty("tokenVersion").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Logout_WithValidTokenAndCookie_ClearsCookie_AndMarksTokenRevoked_Returns204()
    {
        // 1. Login
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand("logout-user@test.com", Password, _tenantId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = loginJson.GetProperty("accessToken").GetString();
        var refreshToken = ExtractRawRefreshToken(loginResponse);

        // 2. Logout
        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        logoutRequest.Headers.Add("Cookie", $"storeos_refresh_token={refreshToken}");

        var logoutResponse = await _client.SendAsync(logoutRequest);
        logoutResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Confirm cookie is cleared in response
        logoutResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var clearedCookie = setCookies!.FirstOrDefault(c => c.StartsWith("storeos_refresh_token="));
        clearedCookie.Should().NotBeNull();
        var normalizedCleared = clearedCookie!.ToLowerInvariant();
        (normalizedCleared.Contains("expires=thu, 01 jan 1970") || normalizedCleared.Contains("max-age=0")).Should().BeTrue();

        // 4. Confirm in DB that the token is marked is_revoked = true
        var hash = RefreshTokenFormat.Hash(refreshToken);
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT is_revoked FROM refresh_tokens WHERE token_hash = @h;", conn);
        cmd.Parameters.AddWithValue("h", hash);
        var isRevoked = (bool)(await cmd.ExecuteScalarAsync())!;
        isRevoked.Should().BeTrue();

        // 5. Attempting to refresh with the logged-out token returns 401
        var refreshRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refreshRequest.Headers.Add("Cookie", $"storeos_refresh_token={refreshToken}");
        var refreshResponse = await _client.SendAsync(refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
