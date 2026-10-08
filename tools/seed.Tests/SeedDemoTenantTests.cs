using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Infrastructure.Identity;
using StoreOS.Seed;

namespace Seed.Tests;

[Collection("seed-postgres")]
public class SeedDemoTenantTests
{
    private readonly SeedPostgresFixture _fixture;
    private readonly Argon2PasswordHasher _hasher = new();

    public SeedDemoTenantTests(SeedPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SeedAsync_CreatesAllRequiredDemoData_WithCorrectCountsAndInvariants()
    {
        await using var db = _fixture.CreateDbContext();

        // Act: Run seed with force = true
        var result = await SeedDemoTenant.SeedAsync(db, tenantName: "Apex Mobiles Demo", subdomain: "apex", force: true);

        // Assert: Result summary
        result.Success.Should().BeTrue();
        result.Skipped.Should().BeFalse();
        result.TenantId.Should().NotBeEmpty();

        var tenantId = result.TenantId;

        // 1. Tenant & Branches
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId);
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be("Apex Mobiles Demo");
        tenant.Subdomain.Should().Be("apex");
        tenant.IsActive.Should().BeTrue();

        using var featuresDoc = JsonDocument.Parse(tenant.FeaturesJson);
        featuresDoc.RootElement.GetProperty("repairs").GetBoolean().Should().BeTrue();

        var branches = await db.Branches.IgnoreQueryFilters().Where(b => b.TenantId == tenantId).ToListAsync();
        branches.Should().HaveCount(2);
        branches.Select(b => b.Code).Should().BeEquivalentTo(new[] { "B01", "B02" });
        branches.Select(b => b.Name).Should().BeEquivalentTo(new[] { "Main Branch", "Mall Branch" });

        // 2. Users (4 users per role, password Demo@12345)
        var users = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenantId).ToListAsync();
        users.Should().HaveCount(4);
        users.Select(u => u.Email).Should().BeEquivalentTo(new[]
        {
            "owner@apex.example",
            "manager@apex.example",
            "cashier@apex.example",
            "technician@apex.example"
        });

        foreach (var u in users)
        {
            _hasher.Verify("Demo@12345", u.PasswordHash).Should().BeTrue();
            u.IsActive.Should().BeTrue();
        }

        // 3. Catalog Products
        var products = await db.Products.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ToListAsync();
        products.Count.Should().BeGreaterThanOrEqualTo(23);

        var newPhones = products.Where(p => p.Category == ProductCategory.NewPhone).ToList();
        newPhones.Count.Should().BeGreaterThanOrEqualTo(5);

        var usedPhones = products.Where(p => p.Category == ProductCategory.UsedPhone).ToList();
        usedPhones.Count.Should().BeGreaterThanOrEqualTo(5);
        usedPhones.Should().OnlyContain(p => p.IsMarginScheme);

        var accessories = products.Where(p => p.Category == ProductCategory.Accessory).ToList();
        accessories.Count.Should().BeGreaterThanOrEqualTo(10);

        var repairServices = products.Where(p => p.Category == ProductCategory.RepairService).ToList();
        repairServices.Count.Should().BeGreaterThanOrEqualTo(3);

        // 4. Stock & Inventories
        var inventories = await db.BranchInventories.IgnoreQueryFilters().Where(bi => bi.TenantId == tenantId).ToListAsync();
        inventories.Count.Should().BeGreaterThanOrEqualTo(40); // 20 physical products * 2 branches

        // At least one product at/below low-stock threshold
        inventories.Should().Contain(bi => bi.StockQty <= bi.LowStockThreshold);

        // At least one product at exactly zero stock
        inventories.Should().Contain(bi => bi.StockQty == 0);

        // 5. Purchases & IMEIs
        var purchases = await db.Purchases.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ToListAsync();
        purchases.Count.Should().BeGreaterThanOrEqualTo(20);

        var imeis = await db.InventoryImeis.IgnoreQueryFilters().Where(i => i.TenantId == tenantId).ToListAsync();
        var inStockImeis = imeis.Where(i => i.Status == "InStock").ToList();
        inStockImeis.Count.Should().BeGreaterThanOrEqualTo(60); // >= 3 per phone product per branch

        // 6. Customers
        var customers = await db.Customers.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).ToListAsync();
        customers.Count.Should().BeGreaterThanOrEqualTo(5);

        // Customer with credit limit set
        customers.Should().Contain(c => c.CreditLimit.HasValue && c.CreditLimit.Value > 0);

        // Customer anonymized
        customers.Should().Contain(c => c.Name == "Anonymized Customer");

        // Customer ledger entries exist
        var customerLedgers = await db.CustomerLedgerEntries.IgnoreQueryFilters().Where(cl => cl.TenantId == tenantId).ToListAsync();
        customerLedgers.Should().NotBeEmpty();

        // 7. Suppliers
        var suppliers = await db.Suppliers.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ToListAsync();
        suppliers.Count.Should().BeGreaterThanOrEqualTo(3);

        var supplierTxns = await db.SupplierTransactions.IgnoreQueryFilters().Where(st => st.TenantId == tenantId).ToListAsync();
        supplierTxns.Should().NotBeEmpty();
        supplierTxns.Should().Contain(st => st.Type == "Bill");
        supplierTxns.Should().Contain(st => st.Type == "Payment");

        var supplierReturns = await db.SupplierReturns.IgnoreQueryFilters().Where(sr => sr.TenantId == tenantId).ToListAsync();
        supplierReturns.Count.Should().BeGreaterThanOrEqualTo(1);

        // 8. Sales
        var sales = await db.Sales.IgnoreQueryFilters().Where(s => s.TenantId == tenantId).ToListAsync();
        sales.Count.Should().BeGreaterThanOrEqualTo(8);

        // Tax invoices spanning multiple days
        var taxInvoices = sales.Where(s => s.InvoiceType == InvoiceType.TaxInvoice).ToList();
        taxInvoices.Count.Should().BeGreaterThanOrEqualTo(7);

        // Estimate exists
        sales.Should().Contain(s => s.InvoiceType == "Estimate");

        // Voided sale exists
        sales.Should().Contain(s => s.Status == "Voided");

        // Refunded sale exists
        sales.Should().Contain(s => s.Status == "Refunded");

        // Financed sale exists
        sales.Should().Contain(s => s.PaymentMode == "Finance" && !string.IsNullOrEmpty(s.FinanceProvider));

        // Customer payment exists
        var payments = await db.CustomerPayments.IgnoreQueryFilters().Where(cp => cp.TenantId == tenantId).ToListAsync();
        payments.Should().NotBeEmpty();

        // 9. Expenses
        var expenses = await db.Expenses.IgnoreQueryFilters().Where(e => e.TenantId == tenantId).ToListAsync();
        expenses.Count.Should().BeGreaterThanOrEqualTo(5);
        expenses.Select(e => e.Category).Distinct().Count().Should().BeGreaterThanOrEqualTo(3);

        // 10. Liabilities
        var liabilities = await db.Liabilities.IgnoreQueryFilters().Where(l => l.TenantId == tenantId).ToListAsync();
        liabilities.Count.Should().BeGreaterThanOrEqualTo(1);

        var liabilityPayments = await db.LiabilityPayments.IgnoreQueryFilters().Where(lp => lp.TenantId == tenantId).ToListAsync();
        liabilityPayments.Count.Should().BeGreaterThanOrEqualTo(1);

        // 11. Repair Jobs Lifecycle
        var repairJobs = await db.RepairJobs.IgnoreQueryFilters().Where(rj => rj.TenantId == tenantId).ToListAsync();
        repairJobs.Count.Should().BeGreaterThanOrEqualTo(7);

        // Each status represented
        repairJobs.Should().Contain(j => j.Status == RepairStatus.Received);
        repairJobs.Should().Contain(j => j.Status == RepairStatus.Diagnosing);
        repairJobs.Should().Contain(j => j.Status == RepairStatus.WaitingForApproval);
        repairJobs.Should().Contain(j => j.Status == RepairStatus.WaitingForParts);
        repairJobs.Should().Contain(j => j.Status == RepairStatus.InRepair);
        repairJobs.Should().Contain(j => j.Status == RepairStatus.ReadyForPickup);
        repairJobs.Should().Contain(j => j.Status == RepairStatus.Delivered);

        // Parts consumed exists
        var parts = await db.RepairJobParts.IgnoreQueryFilters().Where(p => p.TenantId == tenantId).ToListAsync();
        parts.Should().Contain(p => p.Status == RepairPartStatus.Consumed);

        // Billed repair job linked to a sale
        repairJobs.Should().Contain(j => j.SaleId.HasValue && !string.IsNullOrEmpty(j.InvoiceNumber));
    }

    [Fact]
    public async Task SeedAsync_WhenTenantExists_SkipsWithoutForce_AndReseedsWithForce()
    {
        await using var db = _fixture.CreateDbContext();

        // First run
        var firstResult = await SeedDemoTenant.SeedAsync(db, tenantName: "Apex Mobiles Demo", subdomain: "apex", force: true);
        firstResult.Success.Should().BeTrue();
        firstResult.Skipped.Should().BeFalse();

        // Second run without force should skip
        var secondResult = await SeedDemoTenant.SeedAsync(db, tenantName: "Apex Mobiles Demo", subdomain: "apex", force: false);
        secondResult.Success.Should().BeTrue();
        secondResult.Skipped.Should().BeTrue();
        secondResult.Message.Should().Contain("already exists");

        // Third run with force should wipe and reseed
        var thirdResult = await SeedDemoTenant.SeedAsync(db, tenantName: "Apex Mobiles Demo", subdomain: "apex", force: true);
        thirdResult.Success.Should().BeTrue();
        thirdResult.Skipped.Should().BeFalse();
    }
}
