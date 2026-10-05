using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity;
using StoreOS.Application.Tenancy.Branches;
using StoreOS.Application.Tenancy.Branches.CreateBranch;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Tenancy;

[Collection("postgres")]
public class BranchesControllerTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();
    private readonly Guid _branch2Id = Guid.NewGuid();

    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _adminRoleId = Guid.NewGuid();

    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    public BranchesControllerTests(PostgresTestFixture fixture)
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
        var passwordHash = _hasher.Hash("Password123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Branch Test Tenant", subdomain: "bct-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch1 = Branch.Create(_tenantId, "B01", "HQ Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch1, _branch1Id);
        db.Branches.Add(branch1);

        var branch2 = Branch.Create(_tenantId, "B02", "Second Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch2, _branch2Id);
        db.Branches.Add(branch2);

        // Admin Role & User
        var adminRole = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(adminRole, _adminRoleId);
        db.Roles.Add(adminRole);

        var adminPerm = RolePermission.Create(_adminRoleId, "branches:manage");
        db.RolePermissions.Add(adminPerm);

        var adminUser = User.Create(_tenantId, "admin@bctest.com", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(adminUser, _adminUserId);
        db.Users.Add(adminUser);

        var adminUb = UserBranch.Create(_adminUserId, _branch1Id, isDefault: true);
        db.UserBranches.Add(adminUb);

        // Cashier Role & User (no branches:manage, single branch B01)
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        var cashierPerm = RolePermission.Create(_cashierRoleId, "pos:checkout");
        db.RolePermissions.Add(cashierPerm);

        var cashierUser = User.Create(_tenantId, "cashier@bctest.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        var cashierUb = UserBranch.Create(_cashierUserId, _branch1Id, isDefault: true);
        db.UserBranches.Add(cashierUb);

        await db.SaveChangesAsync();

        // Raw SQL inserts for user_role_assignments
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES " +
            "(@aId, @arId, NULL), " +
            "(@cId, @crId, @b1Id);", conn);
        cmd.Parameters.AddWithValue("aId", _adminUserId);
        cmd.Parameters.AddWithValue("arId", _adminRoleId);
        cmd.Parameters.AddWithValue("cId", _cashierUserId);
        cmd.Parameters.AddWithValue("crId", _cashierRoleId);
        cmd.Parameters.AddWithValue("b1Id", _branch1Id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_branches WHERE user_id IN (@aId, @cId); " +
            "DELETE FROM user_role_assignments WHERE user_id IN (@aId, @cId); " +
            "DELETE FROM role_permissions WHERE role_id IN (@arId, @crId); " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("aId", _adminUserId);
        cmd.Parameters.AddWithValue("cId", _cashierUserId);
        cmd.Parameters.AddWithValue("arId", _adminRoleId);
        cmd.Parameters.AddWithValue("crId", _cashierRoleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, bool allBranches, IEnumerable<Guid> branchIds, IEnumerable<string> perms)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches, branchIds, perms, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task CreateBranch_WithBranchesManagePermission_Returns201AndIsRetrievable()
    {
        var token = IssueToken(_adminUserId, allBranches: true, Array.Empty<Guid>(), new[] { "branches:manage" });

        var command = new CreateBranchCommand(
            Code: "B03",
            Name: "North Branch",
            Address: "123 North St",
            Phone: "9876543210",
            Email: "north@store.com",
            Gstin: "27AAAAA0000A1Z5");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/branches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newBranchId = json.GetProperty("id").GetGuid();
        newBranchId.Should().NotBeEmpty();

        // Verify retrievable via GET
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/branches/{newBranchId}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var branchDto = await getResponse.Content.ReadFromJsonAsync<BranchDto>();
        branchDto.Should().NotBeNull();
        branchDto!.Code.Should().Be("B03");
        branchDto.Name.Should().Be("North Branch");
        branchDto.Address.Should().Be("123 North St");
        branchDto.Phone.Should().Be("9876543210");
        branchDto.Email.Should().Be("north@store.com");
        branchDto.Gstin.Should().Be("27AAAAA0000A1Z5");
        branchDto.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBranch_WithoutBranchesManagePermission_Returns403Forbidden()
    {
        // Cashier user has no branches:manage permission
        var token = IssueToken(_cashierUserId, allBranches: false, new[] { _branch1Id }, new[] { "pos:checkout" });

        var command = new CreateBranchCommand(
            Code: "B04",
            Name: "Unauthorized Branch",
            Address: null,
            Phone: null,
            Email: null,
            Gstin: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/branches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Fact]
    public async Task CreateBranch_DuplicateCode_Returns409Conflict()
    {
        var token = IssueToken(_adminUserId, allBranches: true, Array.Empty<Guid>(), new[] { "branches:manage" });

        // Duplicate code B01
        var command = new CreateBranchCommand(
            Code: "B01",
            Name: "Duplicate HQ",
            Address: null,
            Phone: null,
            Email: null,
            Gstin: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/branches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("branch_code_taken");
    }

    [Fact]
    public async Task ListBranches_ScopedToOneBranchOnly_ReturnsOnlyAssignedBranch()
    {
        // Cashier is assigned to B01 only, allBranches = false
        var token = IssueToken(_cashierUserId, allBranches: false, new[] { _branch1Id }, new[] { "pos:checkout" });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/branches");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var branches = await response.Content.ReadFromJsonAsync<List<BranchDto>>();
        branches.Should().NotBeNull();
        branches!.Should().ContainSingle();
        branches![0].Id.Should().Be(_branch1Id);
        branches[0].Code.Should().Be("B01");
    }

    [Fact]
    public async Task UpdateBranch_ProfileFields_ReflectedOnSubsequentGet()
    {
        var token = IssueToken(_adminUserId, allBranches: true, Array.Empty<Guid>(), new[] { "branches:manage" });

        var updateBody = new
        {
            Name = "HQ Main Updated",
            Address = "456 Market St",
            Phone = "1234567890",
            Email = "hq@updated.com",
            Gstin = "27BBBBB1111B2Z6",
            IsActive = true
        };

        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/branches/{_branch1Id}");
        patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        patchRequest.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        patchRequest.Content = JsonContent.Create(updateBody);

        var patchResponse = await _client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Subsequent GET reflects the updated fields
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/branches/{_branch1Id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var branchDto = await getResponse.Content.ReadFromJsonAsync<BranchDto>();
        branchDto.Should().NotBeNull();
        branchDto!.Name.Should().Be("HQ Main Updated");
        branchDto.Address.Should().Be("456 Market St");
        branchDto.Phone.Should().Be("1234567890");
        branchDto.Email.Should().Be("hq@updated.com");
        branchDto.Gstin.Should().Be("27BBBBB1111B2Z6");
        branchDto.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task DeactivateBranch_SetsIsActiveFalse_RowNotDeleted()
    {
        var token = IssueToken(_adminUserId, allBranches: true, Array.Empty<Guid>(), new[] { "branches:manage" });

        var updateBody = new
        {
            Name = "Second Branch",
            Address = (string?)null,
            Phone = (string?)null,
            Email = (string?)null,
            Gstin = (string?)null,
            IsActive = false
        };

        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/branches/{_branch2Id}");
        patchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        patchRequest.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        patchRequest.Content = JsonContent.Create(updateBody);

        var patchResponse = await _client.SendAsync(patchRequest);
        patchResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Subsequent GET reflects IsActive = false
        var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/branches/{_branch2Id}");
        getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var getResponse = await _client.SendAsync(getRequest);
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var branchDto = await getResponse.Content.ReadFromJsonAsync<BranchDto>();
        branchDto.Should().NotBeNull();
        branchDto!.IsActive.Should().BeFalse();

        // Direct DB inspection confirms row still exists in branches table
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT is_active FROM branches WHERE id = @id;", conn);
        cmd.Parameters.AddWithValue("id", _branch2Id);
        var dbIsActive = (bool)(await cmd.ExecuteScalarAsync())!;
        dbIsActive.Should().BeFalse();
    }
}
