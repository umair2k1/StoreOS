using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform.BackupExport;
using StoreOS.Application.Platform.BackupImport;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class ImportTenantDataTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _unprivilegedUserId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _unprivilegedRoleId = Guid.NewGuid();

    private Guid _productId;
    private Guid _saleAId;
    private Guid _saleBId;

    public ImportTenantDataTests(PostgresTestFixture fixture)
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

        // 1. Tenant
        var tenant = Tenant.Create("Import Test Tenant");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        // 2. Branches
        var branchA = Branch.Create(_tenantId, "B01", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "B02", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        // 3. Settings
        var setting = Setting.Create(_tenantId, _branchAId, "invoice.prefix", "\"INV-A\"");
        db.Settings.Add(setting);

        // 4. Roles & Permissions
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

        // 5. Users
        var user = User.Create(_tenantId, "admin@importtest.io", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var unprivilegedUser = User.Create(_tenantId, "viewer@importtest.io", passwordHash, "Viewer User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(unprivilegedUser, _unprivilegedUserId);
        db.Users.Add(unprivilegedUser);

        // 6. User Branches & Role Assignments
        var ubA = UserBranch.Create(_userId, _branchAId, isDefault: true);
        db.UserBranches.Add(ubA);

        var ura = UserRoleAssignment.Create(_userId, _roleId, _branchAId);
        db.UserRoleAssignments.Add(ura);

        var unprivUb = UserBranch.Create(_unprivilegedUserId, _branchAId, isDefault: true);
        db.UserBranches.Add(unprivUb);

        var unprivUra = UserRoleAssignment.Create(_unprivilegedUserId, _unprivilegedRoleId, _branchAId);
        db.UserRoleAssignments.Add(unprivUra);

        // 7. Product & Inventories
        var product = Product.Create(_tenantId, "Import Test Product", ProductCategory.NewPhone, 50000m, 40000m, 18m, "85171200");
        db.Products.Add(product);
        _productId = product.Id;

        var invA = BranchInventory.Create(_tenantId, _branchAId, product.Id);
        invA.IncreaseStock(10, 40000m);
        db.BranchInventories.Add(invA);

        var invB = BranchInventory.Create(_tenantId, _branchBId, product.Id);
        invB.IncreaseStock(5, 40000m);
        db.BranchInventories.Add(invB);

        // 8. Customer
        var customer = Customer.Create(_tenantId, "Customer Bob", "9876543211", null);
        db.Customers.Add(customer);

        // 9. Sales across branches
        var saleA = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-A-101", DateTimeOffset.UtcNow,
            customer.Id, customer.Name, null,
            50000m, 4500m, 4500m, 59000m, 0m, 59000m,
            "Cash", null, null, null, null, null, null, null,
            _userId);
        var itemA = SaleItem.Create(_tenantId, saleA.Id, product.Id, product.Name, product.Category, 1, 50000m, 40000m, "85171200", false, false, 50000m, 4500m, 4500m, "350000000000010");
        saleA.AddItem(itemA);
        db.Sales.Add(saleA);
        _saleAId = saleA.Id;

        var saleB = Sale.CreateTaxInvoice(
            _tenantId, _branchBId, "INV-B-101", DateTimeOffset.UtcNow,
            customer.Id, customer.Name, null,
            50000m, 4500m, 4500m, 59000m, 0m, 59000m,
            "UPI", null, null, null, null, null, null, null,
            _userId);
        var itemB = SaleItem.Create(_tenantId, saleB.Id, product.Id, product.Name, product.Category, 1, 50000m, 40000m, "85171200", false, false, 50000m, 4500m, 4500m, "350000000000020");
        saleB.AddItem(itemB);
        db.Sales.Add(saleB);
        _saleBId = saleB.Id;

        // 10. Customer Payment
        var paymentA = CustomerPayment.Create(_tenantId, saleA.Id, 59000m, "Cash", DateOnly.FromDateTime(DateTime.UtcNow), _userId);
        db.CustomerPayments.Add(paymentA);

        // 11. Expenses
        var expA = Expense.Create(_tenantId, _branchAId, "Rent Branch A", 1000m, "Rent", DateOnly.FromDateTime(DateTime.UtcNow), null, _userId);
        var expB = Expense.Create(_tenantId, _branchBId, "Rent Branch B", 1200m, "Rent", DateOnly.FromDateTime(DateTime.UtcNow), null, _userId);
        db.Expenses.AddRange(expA, expB);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM customer_payments WHERE tenant_id = @tId; " +
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
            "DELETE FROM expenses WHERE tenant_id = @tId; " +
            "DELETE FROM customers WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM settings WHERE tenant_id = @tId; " +
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

    private string IssueToken(Guid userId, Guid roleId, bool hasSettingsManage, Guid branchId)
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
            branchIds: new[] { branchId },
            permissions,
            tokenVersion: 1);

        return token;
    }

    private HttpClient CreateClientForUser(Guid userId, Guid roleId, bool hasSettingsManage, Guid branchId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(userId, roleId, hasSettingsManage, branchId));
        client.DefaultRequestHeaders.Add(BranchContextMiddleware.HeaderName, branchId.ToString());
        return client;
    }

    private async Task<Dictionary<string, int>> GetTableRowCountsAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var counts = new Dictionary<string, int>
        {
            ["branches"] = await db.Branches.CountAsync(b => b.TenantId == _tenantId),
            ["settings"] = await db.Settings.CountAsync(s => s.TenantId == _tenantId),
            ["users"] = await db.Users.CountAsync(u => u.TenantId == _tenantId),
            ["roles"] = await db.Roles.CountAsync(r => r.TenantId == _tenantId),
            ["products"] = await db.Products.CountAsync(p => p.TenantId == _tenantId),
            ["branch_inventory"] = await db.BranchInventories.CountAsync(bi => bi.TenantId == _tenantId),
            ["sales"] = await db.Sales.CountAsync(s => s.TenantId == _tenantId),
            ["sale_items"] = await db.SaleItems.CountAsync(si => si.TenantId == _tenantId),
            ["customers"] = await db.Customers.CountAsync(c => c.TenantId == _tenantId),
            ["customer_payments"] = await db.CustomerPayments.CountAsync(cp => cp.TenantId == _tenantId),
            ["expenses"] = await db.Expenses.CountAsync(e => e.TenantId == _tenantId)
        };

        return counts;
    }

    [Fact]
    public async Task Import_FullRoundTrip_MatchesPreExportCountsExactly()
    {
        // (a) full round-trip: export a tenant's seeded data, import that SAME backup back,
        // and confirm every table's row COUNT matches pre-export state exactly
        using var client = CreateClientForUser(_userId, _roleId, hasSettingsManage: true, _branchAId);

        var preExportCounts = await GetTableRowCountsAsync();

        // 1. Export
        var exportResponse = await client.PostAsync("/api/tenant/export", null);
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var backupJson = await exportResponse.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(backupJson);
        var backupDto = JsonSerializer.Deserialize<TenantBackupDto>(backupJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })!;

        backupDto.Should().NotBeNull();

        // 2. Import
        var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/tenant/import")
        {
            Content = JsonContent.Create(backupDto)
        };
        importRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var importResponse = await client.SendAsync(importRequest);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await importResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();

        // 3. Verify row counts match pre-export exactly
        var postImportCounts = await GetTableRowCountsAsync();
        postImportCounts.Should().BeEquivalentTo(preExportCounts);
    }

    [Fact]
    public async Task Import_UnrecognizedSchemaVersion_FailsCleanlyAndLeavesDatabaseUntouched()
    {
        // (b) importing a backup with unrecognized SchemaVersion fails cleanly with Success: false
        // and database is COMPLETELY UNTOUCHED
        using var client = CreateClientForUser(_userId, _roleId, hasSettingsManage: true, _branchAId);

        var preCounts = await GetTableRowCountsAsync();

        // Export valid backup
        var exportResponse = await client.PostAsync("/api/tenant/export", null);
        var backup = await exportResponse.Content.ReadFromJsonAsync<TenantBackupDto>();

        var badBackup = backup! with { SchemaVersion = "99.0" };

        var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/tenant/import")
        {
            Content = JsonContent.Create(badBackup)
        };
        importRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var importResponse = await client.SendAsync(importRequest);
        importResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await importResponse.Content.ReadFromJsonAsync<ImportResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("unsupported_schema_version");

        // Verify database is completely untouched
        var postCounts = await GetTableRowCountsAsync();
        postCounts.Should().BeEquivalentTo(preCounts);
    }

    [Fact]
    public async Task Import_MidImportFailure_RollsBackEntireTransactionLeavingPreImportDataIntact()
    {
        // (c) simulate failure mid-import (inject a malformed row that violates FK)
        // confirm ENTIRE transaction rolls back, leaving pre-import data fully intact (Invariant 12)
        using var client = CreateClientForUser(_userId, _roleId, hasSettingsManage: true, _branchAId);

        var preCounts = await GetTableRowCountsAsync();

        var exportResponse = await client.PostAsync("/api/tenant/export", null);
        var backup = await exportResponse.Content.ReadFromJsonAsync<TenantBackupDto>();

        // Inject a malformed SaleItem pointing to a nonexistent SaleId
        var malformedSaleItem = new BackupSaleItemDto(
            Guid.NewGuid(), _tenantId, Guid.NewGuid(), _productId, "Ghost Item", "New Phone",
            1, 50000m, 40000m, "85171200", false, false, 50000m, 4500m, 4500m, null
        );

        var corruptedSaleItems = backup!.SaleItems.Concat(new object[] { malformedSaleItem }).ToArray();
        var corruptedBackup = backup with { SaleItems = corruptedSaleItems };

        var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/tenant/import")
        {
            Content = JsonContent.Create(corruptedBackup)
        };
        importRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var importResponse = await client.SendAsync(importRequest);
        // The foreign key violation should fail the import
        importResponse.IsSuccessStatusCode.Should().BeFalse();

        // Invariant 12: Pre-import data remains COMPLETELY intact!
        var postCounts = await GetTableRowCountsAsync();
        postCounts.Should().BeEquivalentTo(preCounts);
    }

    [Fact]
    public async Task Import_WithoutSettingsManagePermission_Returns403()
    {
        // (d) caller without settings:manage -> 403
        using var client = CreateClientForUser(_unprivilegedUserId, _unprivilegedRoleId, hasSettingsManage: false, _branchAId);

        var dummyBackup = new TenantBackupDto(
            "1.0", DateTimeOffset.UtcNow, _tenantId,
            new { }, Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>(), Array.Empty<object>(),
            Array.Empty<object>(), Array.Empty<object>()
        );

        var importRequest = new HttpRequestMessage(HttpMethod.Post, "/api/tenant/import")
        {
            Content = JsonContent.Create(dummyBackup)
        };
        importRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(importRequest);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
