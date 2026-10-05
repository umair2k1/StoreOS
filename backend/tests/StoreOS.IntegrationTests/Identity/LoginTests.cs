using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Identity.Login;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Identity;

[Collection("postgres")]
public class LoginTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _roleAId = Guid.NewGuid();
    private readonly Guid _userAId = Guid.NewGuid();
    private readonly Guid _inactiveUserId = Guid.NewGuid();

    private readonly Guid _inactiveTenantId = Guid.NewGuid();
    private readonly Guid _userInInactiveTenantId = Guid.NewGuid();

    private const string Password = "Password123!";

    public LoginTests(PostgresTestFixture fixture)
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

        // 1. Seed Tenant A (Active)
        var tenantA = Tenant.Create("Login Tenant A", subdomain: "login-a-" + _tenantAId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenantA, _tenantAId);
        db.Tenants.Add(tenantA);

        // 2. Seed Role A with pos:checkout
        var roleA = Role.Create(_tenantAId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(roleA, _roleAId);
        db.Roles.Add(roleA);

        var rolePerm = RolePermission.Create(_roleAId, "pos:checkout");
        db.RolePermissions.Add(rolePerm);

        // 3. Seed User A (Active, tenant-wide branch_id = null)
        var userA = User.Create(_tenantAId, "cashier@test.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userA, _userAId);
        db.Users.Add(userA);


        // 4. Seed Inactive User in Tenant A
        var inactiveUser = User.Create(_tenantAId, "inactive@test.com", passwordHash, "Inactive User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(inactiveUser, _inactiveUserId);
        inactiveUser.Deactivate();
        db.Users.Add(inactiveUser);

        // 5. Seed Inactive Tenant with User
        var inactiveTenant = Tenant.Create("Inactive Tenant", subdomain: "inact-" + _inactiveTenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(inactiveTenant, _inactiveTenantId);
        inactiveTenant.Deactivate();
        db.Tenants.Add(inactiveTenant);

        var userInInactiveTenant = User.Create(_inactiveTenantId, "user@inactive-tenant.com", passwordHash, "Inactive Tenant User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userInInactiveTenant, _userInInactiveTenantId);
        db.Users.Add(userInInactiveTenant);

        await db.SaveChangesAsync();
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, NULL);", conn);
        cmd.Parameters.AddWithValue("uId", _userAId);
        cmd.Parameters.AddWithValue("rId", _roleAId);
        await cmd.ExecuteNonQueryAsync();

    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM refresh_tokens WHERE user_id IN (@u1, @u2, @u3); " +
            "DELETE FROM user_role_assignments WHERE user_id IN (@u1, @u2, @u3); " +
            "DELETE FROM role_permissions WHERE role_id = @r1; " +
            "DELETE FROM roles WHERE tenant_id IN (@t1, @t2); " +
            "DELETE FROM users WHERE tenant_id IN (@t1, @t2); " +
            "DELETE FROM tenants WHERE id IN (@t1, @t2);", conn);

        cmd.Parameters.AddWithValue("u1", _userAId);
        cmd.Parameters.AddWithValue("u2", _inactiveUserId);
        cmd.Parameters.AddWithValue("u3", _userInInactiveTenantId);
        cmd.Parameters.AddWithValue("r1", _roleAId);
        cmd.Parameters.AddWithValue("t1", _tenantAId);
        cmd.Parameters.AddWithValue("t2", _inactiveTenantId);

        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithWellFormedJwt()
    {
        var command = new LoginCommand("cashier@test.com", Password, _tenantAId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: body);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = json.GetProperty("accessToken").GetString();
        accessToken.Should().NotBeNullOrWhiteSpace();

        json.GetProperty("userId").GetGuid().Should().Be(_userAId);
        json.GetProperty("tenantId").GetGuid().Should().Be(_tenantAId);
        json.GetProperty("displayName").GetString().Should().Be("Cashier User");
        json.GetProperty("allBranches").GetBoolean().Should().BeTrue();

        var permissions = json.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToList();
        permissions.Should().Contain("pos:checkout");

        // Validate decoded JWT claims
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(accessToken);

        jwt.Claims.First(c => c.Type == "sub").Value.Should().Be(_userAId.ToString());
        jwt.Claims.First(c => c.Type == "tenant_id").Value.Should().Be(_tenantAId.ToString());
        jwt.Claims.First(c => c.Type == "all_branches").Value.Should().Be("true");
        jwt.Claims.First(c => c.Type == "token_version").Value.Should().Be("1");
        jwt.Claims.Where(c => c.Type == "perm").Select(c => c.Value).Should().Contain("pos:checkout");
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401InvalidCredentials()
    {
        var command = new LoginCommand("cashier@test.com", "WrongPassword999!", _tenantAId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Login_UnknownEmail_Returns401InvalidCredentials()
    {
        var command = new LoginCommand("nonexistent@test.com", Password, _tenantAId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Login_InactiveUser_Returns401InvalidCredentials()
    {
        var command = new LoginCommand("inactive@test.com", Password, _tenantAId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("invalid_credentials");
    }

    [Fact]
    public async Task Login_InactiveTenant_Returns403TenantInactive()
    {
        var command = new LoginCommand("user@inactive-tenant.com", Password, _inactiveTenantId);

        var response = await _client.PostAsJsonAsync("/api/auth/login", command);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("tenant_inactive");
    }
}
