using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Seed;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class RestoreDrillReconciliationTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;

    public RestoreDrillReconciliationTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private StoreOsDbContext CreateOwnerDbContext()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        return new StoreOsDbContext(options, new NullAccessScope());
    }

    [Fact]
    public async Task RestoreDrill_ExportWipeImport_ReconciliationMatchesPreWipeSnapshot()
    {
        await using var db = CreateOwnerDbContext();

        var tenantName = "Restore Drill Tenant " + Guid.NewGuid().ToString("N")[..6];
        var subdomain = "drill-" + Guid.NewGuid().ToString("N")[..8];

        // 1. Seed demo tenant
        var seedResult = await SeedDemoTenant.SeedAsync(db, tenantName, subdomain, force: true);
        seedResult.Success.Should().BeTrue();
        var tenantId = seedResult.TenantId;

        // 2. Pre-wipe snapshot: row counts, ledger sums, stock quantities
        var preCounts = await CaptureTenantCountsAsync(db, tenantId);
        var preCustomerLedgerSum = await db.CustomerLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .SumAsync(e => e.Debit - e.Credit);
        var preSupplierLedgerSum = await db.SupplierLedgerEntries.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .SumAsync(e => e.Credit - e.Debit);
        var preTotalStockQty = await db.BranchInventories.IgnoreQueryFilters()
            .Where(bi => bi.TenantId == tenantId)
            .SumAsync(bi => bi.StockQty);

        preCounts["products"].Should().BeGreaterThan(0);
        preCounts["sales"].Should().BeGreaterThan(0);
        preCounts["customers"].Should().BeGreaterThan(0);
        preCounts["suppliers"].Should().BeGreaterThan(0);

        var tempBackupPath = Path.Combine(Path.GetTempPath(), $"restore-drill-{tenantId:N}.json");

        try
        {
            // 3. Export backup via CLI mode in StoreOS.Api
            var exportArgs = new[] { "--export-backup-and-exit", "--backup-path", tempBackupPath, "--tenant-id", tenantId.ToString(), "--connection", _fixture.OwnerConnectionString };
            var exportExitCode = await Program.ExecuteCliModeAsync(exportArgs, _factory.Services);
            exportExitCode.Should().Be(0);
            File.Exists(tempBackupPath).Should().BeTrue();

            // 4. Wipe tenant data completely
            await db.ExecuteRawSqlAsync("DELETE FROM public.inventory_imeis WHERE tenant_id = {0}", new object[] { tenantId });
            await db.ExecuteRawSqlAsync("DELETE FROM public.purchases WHERE tenant_id = {0}", new object[] { tenantId });
            await db.ExecuteRawSqlAsync("DELETE FROM public.invoice_sequences WHERE tenant_id = {0}", new object[] { tenantId });
            await db.ExecuteRawSqlAsync("SELECT public.delete_tenant_data_for_restore({0})", new object[] { tenantId });
            await db.ExecuteRawSqlAsync("DELETE FROM public.tenants WHERE id = {0}", new object[] { tenantId });

            // Verify wiped state
            var wipedCounts = await CaptureTenantCountsAsync(db, tenantId);
            wipedCounts.Values.Should().OnlyContain(count => count == 0);

            // 5. Restore from backup via CLI mode in StoreOS.Api
            var importArgs = new[] { "--import-backup-and-exit", "--backup-path", tempBackupPath, "--connection", _fixture.OwnerConnectionString };
            var importExitCode = await Program.ExecuteCliModeAsync(importArgs, _factory.Services);
            importExitCode.Should().Be(0);
            // 6. Post-restore reconciliation: exact match on counts, ledger sums, stock quantities
            var postCounts = await CaptureTenantCountsAsync(db, tenantId);
            postCounts.Should().BeEquivalentTo(preCounts);

            var postCustomerLedgerSum = await db.CustomerLedgerEntries.IgnoreQueryFilters()
                .Where(e => e.TenantId == tenantId)
                .SumAsync(e => e.Debit - e.Credit);
            postCustomerLedgerSum.Should().Be(preCustomerLedgerSum);

            var postSupplierLedgerSum = await db.SupplierLedgerEntries.IgnoreQueryFilters()
                .Where(e => e.TenantId == tenantId)
                .SumAsync(e => e.Credit - e.Debit);
            postSupplierLedgerSum.Should().Be(preSupplierLedgerSum);

            var postTotalStockQty = await db.BranchInventories.IgnoreQueryFilters()
                .Where(bi => bi.TenantId == tenantId)
                .SumAsync(bi => bi.StockQty);
            postTotalStockQty.Should().Be(preTotalStockQty);

            // 7. Verify RLS visibility through app connection (storeos_app role)
            var scope = new TestAccessScope(tenantId);
            var appOptions = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(_fixture.AppConnectionString)
                .AddInterceptors(new RlsSessionInterceptor(scope))
                .Options;

            await using var appDb = new StoreOsDbContext(appOptions, scope);
            await appDb.Database.BeginTransactionAsync();

            var rlsProducts = await appDb.Products.ToListAsync();
            rlsProducts.Count.Should().Be(preCounts["products"]);

            var rlsRoles = await appDb.Roles.ToListAsync();
            rlsRoles.Count.Should().Be(preCounts["roles"]);

            var rlsSales = await appDb.Sales.ToListAsync();
            rlsSales.Count.Should().Be(preCounts["sales"]);
        }
        finally
        {
            if (File.Exists(tempBackupPath))
            {
                File.Delete(tempBackupPath);
            }
        }
    }

    private static async Task<Dictionary<string, int>> CaptureTenantCountsAsync(StoreOsDbContext db, Guid tenantId)
    {
        return new Dictionary<string, int>
        {
            ["tenants"] = await db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Id == tenantId),
            ["branches"] = await db.Branches.IgnoreQueryFilters().CountAsync(b => b.TenantId == tenantId),
            ["roles"] = await db.Roles.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenantId),
            ["users"] = await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenantId),
            ["products"] = await db.Products.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId),
            ["branch_inventory"] = await db.BranchInventories.IgnoreQueryFilters().CountAsync(bi => bi.TenantId == tenantId),
            ["purchases"] = await db.Purchases.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId),
            ["inventory_imeis"] = await db.InventoryImeis.IgnoreQueryFilters().CountAsync(i => i.TenantId == tenantId),
            ["customers"] = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId),
            ["customer_ledger_entries"] = await db.CustomerLedgerEntries.IgnoreQueryFilters().CountAsync(cl => cl.TenantId == tenantId),
            ["suppliers"] = await db.Suppliers.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId),
            ["supplier_transactions"] = await db.SupplierTransactions.IgnoreQueryFilters().CountAsync(st => st.TenantId == tenantId),
            ["supplier_ledger_entries"] = await db.SupplierLedgerEntries.IgnoreQueryFilters().CountAsync(sl => sl.TenantId == tenantId),
            ["sales"] = await db.Sales.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId),
            ["sale_items"] = await db.SaleItems.IgnoreQueryFilters().CountAsync(si => si.TenantId == tenantId),
            ["expenses"] = await db.Expenses.IgnoreQueryFilters().CountAsync(e => e.TenantId == tenantId),
            ["liabilities"] = await db.Liabilities.IgnoreQueryFilters().CountAsync(l => l.TenantId == tenantId),
            ["repair_jobs"] = await db.RepairJobs.IgnoreQueryFilters().CountAsync(rj => rj.TenantId == tenantId)
        };
    }

    private class TestAccessScope : IAccessScope
    {
        public TestAccessScope(Guid tenantId) => TenantId = tenantId;
        public Guid TenantId { get; }
        public bool HasTenantContext => true;
        public Guid UserId { get; } = Guid.NewGuid();
        public IReadOnlySet<Guid> BranchIds { get; } = new HashSet<Guid>();
        public bool AllBranches => true;
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string> { "settings:manage", "pos:checkout" };
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;
        public bool HasBranchAccess(Guid branchId) => true;
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }
}
