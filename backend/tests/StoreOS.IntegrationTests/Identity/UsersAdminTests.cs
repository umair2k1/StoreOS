using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Identity.Login;
using StoreOS.Application.Identity.Roles;
using StoreOS.Application.Identity.Roles.CreateRole;
using StoreOS.Application.Identity.Users;
using StoreOS.Application.Identity.Users.CreateUser;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Identity;

[Collection("postgres")]
public class UsersAdminTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _adminRoleId = Guid.NewGuid();

    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    private const string AdminPassword = "AdminPassword123!";
    private const string CashierPassword = "CashierPassword123!";

    public UsersAdminTests(PostgresTestFixture fixture)
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
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Users Admin Tenant", subdomain: "uat-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "B01", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Admin Role with users:manage
        var adminRole = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(adminRole, _adminRoleId);
        db.Roles.Add(adminRole);

        var adminPerm = RolePermission.Create(_adminRoleId, "users:manage");
        db.RolePermissions.Add(adminPerm);

        var adminUser = User.Create(_tenantId, "admin@uat.com", _hasher.Hash(AdminPassword), "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(adminUser, _adminUserId);
        db.Users.Add(adminUser);

        var adminUb = UserBranch.Create(_adminUserId, _branchId, isDefault: true);
        db.UserBranches.Add(adminUb);

        // Cashier Role with pos:checkout (no users:manage)
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        var cashierPerm = RolePermission.Create(_cashierRoleId, "pos:checkout");
        db.RolePermissions.Add(cashierPerm);

        var cashierUser = User.Create(_tenantId, "cashier@uat.com", _hasher.Hash(CashierPassword), "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        var cashierUb = UserBranch.Create(_cashierUserId, _branchId, isDefault: true);
        db.UserBranches.Add(cashierUb);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES " +
            "(@aId, @arId, NULL), " +
            "(@cId, @crId, @bId);", conn);
        cmd.Parameters.AddWithValue("aId", _adminUserId);
        cmd.Parameters.AddWithValue("arId", _adminRoleId);
        cmd.Parameters.AddWithValue("cId", _cashierUserId);
        cmd.Parameters.AddWithValue("crId", _cashierRoleId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_branches WHERE user_id IN (SELECT id FROM users WHERE tenant_id = @tId); " +
            "DELETE FROM user_role_assignments WHERE user_id IN (SELECT id FROM users WHERE tenant_id = @tId); " +
            "DELETE FROM role_permissions WHERE role_id IN (SELECT id FROM roles WHERE tenant_id = @tId); " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM refresh_tokens WHERE user_id IN (SELECT id FROM users WHERE tenant_id = @tId); " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<string> LoginAndGetTokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginCommand(email, password, _tenantId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task CreateRole_WithValidPermissions_Returns201()
    {
        var adminToken = await LoginAndGetTokenAsync("admin@uat.com", AdminPassword);

        var command = new CreateRoleCommand("Supervisor", new[] { "sales:void", "pos:checkout" });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/roles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var roleId = json.GetProperty("id").GetGuid();
        roleId.Should().NotBeEmpty();

        // Verify in GET /api/roles
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/roles");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var roles = await getResponse.Content.ReadFromJsonAsync<List<RoleDto>>();
        roles.Should().NotBeNull();
        var created = roles!.FirstOrDefault(r => r.Id == roleId);
        created.Should().NotBeNull();
        created!.Name.Should().Be("Supervisor");
        created.Permissions.Should().Contain(new[] { "sales:void", "pos:checkout" });
    }

    [Fact]
    public async Task CreateRole_WithUnknownPermissionCode_Returns400InvalidPermissionCode()
    {
        var adminToken = await LoginAndGetTokenAsync("admin@uat.com", AdminPassword);

        var command = new CreateRoleCommand("InvalidRole", new[] { "not:a:real:permission" });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/roles");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("invalid_permission_code");
    }

    [Fact]
    public async Task CreateUser_AndAssignRoleTenantWide_LoginIssuesJwtWithAssignedPermissions()
    {
        var adminToken = await LoginAndGetTokenAsync("admin@uat.com", AdminPassword);

        // 1. Create a user
        var createCommand = new CreateUserCommand(
            Email: "operator@uat.com",
            DisplayName: "Operator User",
            Password: "OperatorPassword123!");

        var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        createRequest.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        createRequest.Content = JsonContent.Create(createCommand);

        var createResponse = await _client.SendAsync(createRequest);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var createJson = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var newUserId = createJson.GetProperty("id").GetGuid();

        // 2. Assign role tenant-wide (branch_id = null)
        var assignRoleBody = new { RoleId = _adminRoleId, BranchId = (Guid?)null };
        var assignRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{newUserId}/roles");
        assignRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        assignRequest.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        assignRequest.Content = JsonContent.Create(assignRoleBody);

        var assignResponse = await _client.SendAsync(assignRequest);
        assignResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. User subsequent login produces JWT with assigned permissions
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginCommand("operator@uat.com", "OperatorPassword123!", _tenantId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginJson = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginJson.GetProperty("accessToken").GetString()!;

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        jwt.Claims.First(c => c.Type == "sub").Value.Should().Be(newUserId.ToString());
        jwt.Claims.First(c => c.Type == "all_branches").Value.Should().Be("true");
        jwt.Claims.Where(c => c.Type == "perm").Select(c => c.Value).Should().Contain("users:manage");
    }

    [Fact]
    public async Task AssignBranch_WithIsDefaultTrue_GetUsersReflectsDefaultBranch()
    {
        var adminToken = await LoginAndGetTokenAsync("admin@uat.com", AdminPassword);

        // Assign branch to cashier user with isDefault = true
        var assignBranchBody = new { BranchId = _branchId, IsDefault = true };
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/branches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(assignBranchBody);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Call GET /api/users to verify
        var getRequest = new HttpRequestMessage(HttpMethod.Get, "/api/users");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await getResponse.Content.ReadFromJsonAsync<List<UserDto>>();
        users.Should().NotBeNull();
        var cashier = users!.First(u => u.Id == _cashierUserId);
        var defaultBranch = cashier.Branches.FirstOrDefault(b => b.BranchId == _branchId);
        defaultBranch.Should().NotBeNull();
        defaultBranch!.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateUser_BumpsTokenVersion_AndRejectsSubsequentAccess()
    {
        var adminToken = await LoginAndGetTokenAsync("admin@uat.com", AdminPassword);
        var cashierToken = await LoginAndGetTokenAsync("cashier@uat.com", CashierPassword);

        // Deactivate cashier
        var deactivateRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/deactivate");
        deactivateRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        deactivateRequest.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var deactivateResponse = await _client.SendAsync(deactivateRequest);
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify DB: is_active = false, token_version = 2
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT is_active, token_version FROM users WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", _cashierUserId);
        await using var reader = await cmd.ExecuteReaderAsync();
        reader.Read().Should().BeTrue();
        reader.GetBoolean(0).Should().BeFalse();
        reader.GetInt32(1).Should().Be(2);
        await reader.CloseAsync();

        // Attempting to log in as deactivated cashier returns 401 invalid_credentials
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginCommand("cashier@uat.com", CashierPassword, _tenantId));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CallerWithoutUsersManage_CannotCreateUserRoleOrAssignment_Returns403Forbidden()
    {
        var cashierToken = await LoginAndGetTokenAsync("cashier@uat.com", CashierPassword);

        // 1. Create Role
        var roleReq = new HttpRequestMessage(HttpMethod.Post, "/api/roles")
        {
            Content = JsonContent.Create(new CreateRoleCommand("HackerRole", new[] { "sales:void" }))
        };
        roleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        roleReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        var roleResp = await _client.SendAsync(roleReq);
        roleResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Create User
        var userReq = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new CreateUserCommand("hacker@test.com", "Hacker", "Password123!"))
        };
        userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        userReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        var userResp = await _client.SendAsync(userReq);
        userResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Assign Role
        var assignRoleReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/roles")
        {
            Content = JsonContent.Create(new { RoleId = _adminRoleId, BranchId = (Guid?)null })
        };
        assignRoleReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        assignRoleReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        var assignRoleResp = await _client.SendAsync(assignRoleReq);
        assignRoleResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4. Assign Branch
        var assignBranchReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_cashierUserId}/branches")
        {
            Content = JsonContent.Create(new { BranchId = _branchId, IsDefault = false })
        };
        assignBranchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        assignBranchReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        var assignBranchResp = await _client.SendAsync(assignBranchReq);
        assignBranchResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Deactivate
        var deactReq = new HttpRequestMessage(HttpMethod.Post, $"/api/users/{_adminUserId}/deactivate");
        deactReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
        deactReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        var deactResp = await _client.SendAsync(deactReq);
        deactResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
