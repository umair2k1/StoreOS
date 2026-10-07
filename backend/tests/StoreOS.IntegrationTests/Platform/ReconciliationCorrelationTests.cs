using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Jobs;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class ReconciliationCorrelationTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _unprivilegedUserId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _unprivilegedRoleId = Guid.NewGuid();

    private Guid _inventoryId;

    public ReconciliationCorrelationTests(PostgresTestFixture fixture)
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
        var passwordHash = _hasher.Hash("SecretPassword123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Correlation Test Tenant");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "B01", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "Tenant Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, PermissionCatalog.SettingsManage);
        db.RolePermissions.Add(perm);

        var unprivilegedRole = Role.Create(_tenantId, "Viewer");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(unprivilegedRole, _unprivilegedRoleId);
        db.Roles.Add(unprivilegedRole);

        var unprivPerm = RolePermission.Create(_unprivilegedRoleId, PermissionCatalog.InventoryManage);
        db.RolePermissions.Add(unprivPerm);

        var user = User.Create(_tenantId, "admin@corrtest.io", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var unprivilegedUser = User.Create(_tenantId, "viewer@corrtest.io", passwordHash, "Viewer User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(unprivilegedUser, _unprivilegedUserId);
        db.Users.Add(unprivilegedUser);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        var ura = UserRoleAssignment.Create(_userId, _roleId, _branchId);
        db.UserRoleAssignments.Add(ura);

        var unprivUb = UserBranch.Create(_unprivilegedUserId, _branchId, isDefault: true);
        db.UserBranches.Add(unprivUb);

        var unprivUra = UserRoleAssignment.Create(_unprivilegedUserId, _unprivilegedRoleId, _branchId);
        db.UserRoleAssignments.Add(unprivUra);

        // Seed product with deliberate stock drift (stock_qty = 25, 0 movements)
        var product = Product.Create(_tenantId, "Correlation Phone", ProductCategory.NewPhone, 1000m, 800m, 18m, "85171200");
        db.Products.Add(product);

        var inv = BranchInventory.Create(_tenantId, _branchId, product.Id);
        inv.IncreaseStock(25, 800m);
        db.BranchInventories.Add(inv);
        _inventoryId = inv.Id;

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM audit_log WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM user_role_assignments WHERE user_id = @uId OR user_id = @unprivUId; " +
            "DELETE FROM user_branches WHERE user_id = @uId OR user_id = @unprivUId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId OR role_id = @unprivRId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);

        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("unprivUId", _unprivilegedUserId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("unprivRId", _unprivilegedRoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, bool hasSettingsManage)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permissions = hasSettingsManage
            ? new[] { PermissionCatalog.SettingsManage }
            : new[] { PermissionCatalog.InventoryManage };

        var (token, _) = jwtService.IssueAccessToken(
            userId,
            _tenantId,
            allBranches: true,
            branchIds: new[] { _branchId },
            permissions,
            tokenVersion: 1);

        return token;
    }

    private HttpClient CreateClientForUser(Guid userId, bool hasSettingsManage)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(userId, hasSettingsManage));
        client.DefaultRequestHeaders.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        return client;
    }

    [Fact]
    public async Task RunNow_WithExplicitCorrelationIdHeader_PropagatesCorrelationIdIntoAuditLog()
    {
        // (a) triggering run-now with a KNOWN X-Correlation-ID header results in audit_log
        // entries carrying that SAME correlation id
        var knownCorrelationId = Guid.NewGuid().ToString("D");

        using var client = CreateClientForUser(_userId, hasSettingsManage: true);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/platform/reconciliation/run-now");
        request.Headers.Add("X-Correlation-ID", knownCorrelationId);

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("correlationId").GetString().Should().Be(knownCorrelationId);

        // Run the job synchronously in the test using that same correlationId for deterministic execution
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReconciliationJob>();
        await job.RunAsync(knownCorrelationId, CancellationToken.None);

        // Verify audit log entry has the exact matching correlation id
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var driftEntries = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId && a.Action == "reconciliation.drift_detected")
            .ToListAsync();

        driftEntries.Should().NotBeEmpty();
        var entry = driftEntries.First(d => d.EntityId == _inventoryId.ToString());
        entry.CorrelationId.Should().Be(knownCorrelationId);
    }

    [Fact]
    public async Task ScheduledRun_GeneratesDistinctFreshCorrelationId()
    {
        // (b) a SCHEDULED (non-request-triggered) run generates its OWN fresh correlation id distinct from prior runs
        var corr1 = "cron-run-1-" + Guid.NewGuid().ToString("D");
        var corr2 = "cron-run-2-" + Guid.NewGuid().ToString("D");

        corr1.Should().NotBe(corr2);

        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReconciliationJob>();

        // Run 1
        await job.RunAsync(corr1, CancellationToken.None);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var drifts1 = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId && a.CorrelationId == corr1)
            .ToListAsync();
        drifts1.Should().NotBeEmpty();

        // Run 2
        await job.RunAsync(corr2, CancellationToken.None);

        var drifts2 = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId && a.CorrelationId == corr2)
            .ToListAsync();
        drifts2.Should().NotBeEmpty();

        // Confirm correlation IDs are distinct
        drifts1.First().CorrelationId.Should().NotBe(drifts2.First().CorrelationId);
    }

    [Fact]
    public async Task RunNow_WithoutSettingsManagePermission_Returns403()
    {
        // Caller without settings:manage receives 403
        using var client = CreateClientForUser(_unprivilegedUserId, hasSettingsManage: false);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/platform/reconciliation/run-now");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
