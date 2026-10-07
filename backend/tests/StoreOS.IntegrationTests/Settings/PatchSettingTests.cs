using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Settings;

[Collection("postgres")]
public class PatchSettingTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _adminRoleId = Guid.NewGuid();
    private readonly Guid _plainUserId = Guid.NewGuid();
    private readonly Guid _plainRoleId = Guid.NewGuid();

    private string _adminToken = string.Empty;
    private string _plainToken = string.Empty;

    public PatchSettingTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Settings Test Tenant", subdomain: "sett-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "ST1", "Settings Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Admin role with settings:manage
        var adminRole = Role.Create(_tenantId, "AdminRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(adminRole, _adminRoleId);
        db.Roles.Add(adminRole);
        db.RolePermissions.Add(RolePermission.Create(_adminRoleId, "settings:manage"));

        var adminUser = User.Create(_tenantId, "admin@settingstest.com", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(adminUser, _adminUserId);
        db.Users.Add(adminUser);
        db.UserBranches.Add(UserBranch.Create(_adminUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_adminUserId, _adminRoleId, _branchId));

        // Plain user without settings:manage
        var plainRole = Role.Create(_tenantId, "PlainRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(plainRole, _plainRoleId);
        db.Roles.Add(plainRole);

        var plainUser = User.Create(_tenantId, "plain@settingstest.com", passwordHash, "Plain User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(plainUser, _plainUserId);
        db.Users.Add(plainUser);
        db.UserBranches.Add(UserBranch.Create(_plainUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_plainUserId, _plainRoleId, _branchId));

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var (adminToken, _) = jwtService.IssueAccessToken(
            _adminUserId, _tenantId, allBranches: true, new[] { _branchId },
            new[] { "settings:manage" }, tokenVersion: 1);
        _adminToken = adminToken;

        var (plainToken, _) = jwtService.IssueAccessToken(
            _plainUserId, _tenantId, allBranches: true, new[] { _branchId },
            Array.Empty<string>(), tokenVersion: 1);
        _plainToken = plainToken;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM settings WHERE tenant_id = @t;
            DELETE FROM user_role_assignments WHERE user_id = @u OR user_id = @pu;
            DELETE FROM user_branches WHERE user_id = @u OR user_id = @pu;
            DELETE FROM users WHERE tenant_id = @t;
            DELETE FROM role_permissions WHERE role_id = @r;
            DELETE FROM roles WHERE tenant_id = @t;
            DELETE FROM branches WHERE tenant_id = @t;
            DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("u", _adminUserId);
        cmd.Parameters.AddWithValue("pu", _plainUserId);
        cmd.Parameters.AddWithValue("r", _adminRoleId);
        await cmd.ExecuteNonQueryAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, object body, string token)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var json = JsonSerializer.Serialize(body);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }

    [Fact]
    public async Task PatchSetting_CreateIfAbsent_ThenUpdateIfPresent_Succeeds()
    {
        const string key = "shop_profile";
        var initialPayload = new
        {
            name = "Apex Store",
            address = "123 High Street",
            phone = "9876543210",
            gstin = "29ABCDE1234F1Z5"
        };
        // 1. Create-if-absent
        var createReq = CreateRequest(HttpMethod.Patch, $"/api/settings/{key}", initialPayload, _adminToken);
        var createRes = await _client.SendAsync(createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify in DB directly
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var conn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value::text FROM settings WHERE tenant_id = @t AND branch_id IS NULL AND key = @k";
            cmd.Parameters.AddWithValue("t", _tenantId);
            cmd.Parameters.AddWithValue("k", key);
            var val = (string?)await cmd.ExecuteScalarAsync();
            val.Should().NotBeNull();
            val.Should().Contain("Apex Store");
        }

        // 2. Update-if-present
        var updatedPayload = new
        {
            name = "Apex Superstore",
            address = "456 Main Avenue",
            phone = "9876543210",
            gstin = "29ABCDE1234F1Z5"
        };
        var updateReq = CreateRequest(HttpMethod.Patch, $"/api/settings/{key}", updatedPayload, _adminToken);
        var updateRes = await _client.SendAsync(updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify in DB directly
        await using (var conn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value::text FROM settings WHERE tenant_id = @t AND branch_id IS NULL AND key = @k";
            cmd.Parameters.AddWithValue("t", _tenantId);
            cmd.Parameters.AddWithValue("k", key);
            var val = (string?)await cmd.ExecuteScalarAsync();
            val.Should().NotBeNull();
            val.Should().Contain("Apex Superstore");
        }
    }

    [Fact]
    public async Task PatchSetting_WithBranchId_ScopingWorks()
    {
        const string key = "branch_receipt_footer";
        var payload = new { footer = "Thank you for visiting ST1!" };

        var req = CreateRequest(HttpMethod.Patch, $"/api/settings/{key}?branchId={_branchId}", payload, _adminToken);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        {
            var setting = await db.Settings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.TenantId == _tenantId && s.Key == key && s.BranchId == _branchId);

            setting.Should().NotBeNull();
            setting!.ValueJson.Should().Contain("Thank you for visiting ST1!");
        }
    }

    [Fact]
    public async Task PatchSetting_WithoutPermission_ReturnsForbidden()
    {
        var payload = new { dummy = "value" };
        var req = CreateRequest(HttpMethod.Patch, "/api/settings/shop_profile", payload, _plainToken);
        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
