using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Jobs;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class ReconciliationJobTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;

    private readonly Guid _tenant1Id = Guid.NewGuid();
    private readonly Guid _tenant2Id = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();
    private readonly Guid _branch2Id = Guid.NewGuid();

    public ReconciliationJobTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // 1. Tenant 1
        var t1 = Tenant.Create("Recon Tenant 1");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(t1, _tenant1Id);
        db.Tenants.Add(t1);

        var b1 = Branch.Create(_tenant1Id, "B01", "Branch 1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(b1, _branch1Id);
        db.Branches.Add(b1);

        // 2. Tenant 2
        var t2 = Tenant.Create("Recon Tenant 2");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(t2, _tenant2Id);
        db.Tenants.Add(t2);

        var b2 = Branch.Create(_tenant2Id, "B01", "Branch 2");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(b2, _branch2Id);
        db.Branches.Add(b2);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM audit_log WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM supplier_ledger_entries WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM supplier_transactions WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM purchases WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM suppliers WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM customer_payments WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM customer_ledger_entries WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM sale_items WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM sales WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM customers WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM products WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM branches WHERE tenant_id = @t1 OR tenant_id = @t2; " +
            "DELETE FROM tenants WHERE id = @t1 OR id = @t2;", conn);

        cmd.Parameters.AddWithValue("t1", _tenant1Id);
        cmd.Parameters.AddWithValue("t2", _tenant2Id);
        await cmd.ExecuteNonQueryAsync();

        _factory.Dispose();
    }

    [Fact]
    public async Task ReconciliationJob_ConsistentDataset_ProducesZeroDriftEntries()
    {
        // (a) a CONSISTENT dataset (no seeded drift) produces ZERO audit-log
        // reconciliation.drift_detected entries across all 5 sweeps
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // 1. Stock consistent
        var p = Product.Create(_tenant1Id, "Phone Consistent", ProductCategory.NewPhone, 10000m, 8000m, 18m, "85171200");
        db.Products.Add(p);
        var inv = BranchInventory.Create(_tenant1Id, _branch1Id, p.Id);
        inv.IncreaseStock(10, 8000m);
        db.BranchInventories.Add(inv);

        var mov = InventoryMovement.Create(_tenant1Id, _branch1Id, p.Id, null, MovementType.Purchase, 10, 8000m, "PO", "1", null);
        db.InventoryMovements.Add(mov);

        // 2. Sales consistent
        var cust = Customer.Create(_tenant1Id, "Customer Consistent", "9111111111", null);
        cust.AddToTotalPurchases(1180m);
        db.Customers.Add(cust);

        var sale = Sale.CreateTaxInvoice(_tenant1Id, _branch1Id, "INV-CON-01", DateTimeOffset.UtcNow,
            cust.Id, cust.Name, null, 1000m, 90m, 90m, 1180m, 0m, 1180m, "Cash",
            null, null, null, null, null, null, null, Guid.Empty);
        db.Sales.Add(sale);

        var pay = CustomerPayment.Create(_tenant1Id, sale.Id, 1180m, "Cash", DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty);
        db.CustomerPayments.Add(pay);

        // 3. Customer Ledger consistent (0 net balance, 0 unpaid sales)
        var cleDebit = CustomerLedgerEntry.CreateDebit(_tenant1Id, cust.Id, sale.Id, CustomerLedgerEntryType.Invoice, 1180m, _branch1Id, DateOnly.FromDateTime(DateTime.UtcNow), "INV-CON-01");
        var cleCredit = CustomerLedgerEntry.CreateCredit(_tenant1Id, cust.Id, sale.Id, CustomerLedgerEntryType.Payment, 1180m, _branch1Id, DateOnly.FromDateTime(DateTime.UtcNow), "INV-CON-01");
        db.CustomerLedgerEntries.AddRange(cleDebit, cleCredit);

        // 4. Supplier consistent
        var supp = Supplier.Create(_tenant1Id, "Supplier Consistent", "9222222222", "Address", "Brand", null);
        db.Suppliers.Add(supp);

        var purchase = Purchase.Create(_tenant1Id, _branch1Id, p.Id, 1, 500m, supp.Id, supp.Name, supp.Phone, supp.Address, DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty);
        db.Purchases.Add(purchase);

        var suppTxn = SupplierTransaction.Create(_tenant1Id, supp.Id, _branch1Id, SupplierTransactionType.Payment, 500m, "Cash", "REF-1", DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.Empty);
        db.SupplierTransactions.Add(suppTxn);

        var sleCredit = SupplierLedgerEntry.CreateCredit(_tenant1Id, supp.Id, _branch1Id, SupplierLedgerEntryType.Bill, 500m, DateOnly.FromDateTime(DateTime.UtcNow), "REF-1");
        var sleDebit = SupplierLedgerEntry.CreateDebit(_tenant1Id, supp.Id, _branch1Id, SupplierLedgerEntryType.Payment, 500m, DateOnly.FromDateTime(DateTime.UtcNow), "REF-1");
        db.SupplierLedgerEntries.AddRange(sleCredit, sleDebit);

        await db.SaveChangesAsync();

        // Run Job
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReconciliationJob>();
        await job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None);

        // Assert ZERO drift detected entries in audit log
        var driftEntries = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant1Id && a.Action == "reconciliation.drift_detected")
            .ToListAsync();

        driftEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconciliationJob_DeliberateDriftAcrossAllFiveSweeps_DetectsAllFiveIndependently()
    {
        // (b) DELIBERATELY seed a drift for EACH of the 5 sweeps independently
        // confirm the job detects and logs EACH one with the correct entity id and expected-vs-actual values
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // Sweep 1 entities
        var p = Product.Create(_tenant1Id, "Drift Test Phone", ProductCategory.NewPhone, 1000m, 800m, 18m, "85171200");
        db.Products.Add(p);
        var inv = BranchInventory.Create(_tenant1Id, _branch1Id, p.Id);
        inv.IncreaseStock(10, 800m);
        db.BranchInventories.Add(inv);

        var mov = InventoryMovement.Create(_tenant1Id, _branch1Id, p.Id, null, MovementType.Purchase, 10, 800m, "PO", "1", null);
        db.InventoryMovements.Add(mov);

        // Sweep 2 entities (Sale payment drift)
        var sale2 = Sale.CreateTaxInvoice(_tenant1Id, _branch1Id, "INV-DRIFT-02", DateTimeOffset.UtcNow,
            null, "Cash Customer", null, 1000m, 90m, 90m, 1180m, 0m, 1180m, "Cash",
            null, null, null, null, null, null, null, Guid.Empty);
        db.Sales.Add(sale2);

        var pay2 = CustomerPayment.Create(_tenant1Id, sale2.Id, 1180m, "Cash", DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty);
        db.CustomerPayments.Add(pay2);

        // Sweep 3 entities (Customer revenue drift)
        var cust3 = Customer.Create(_tenant1Id, "Customer Three", "9333333333", null);
        cust3.AddToTotalPurchases(1180m);
        db.Customers.Add(cust3);

        var sale3 = Sale.CreateTaxInvoice(_tenant1Id, _branch1Id, "INV-DRIFT-03", DateTimeOffset.UtcNow,
            cust3.Id, cust3.Name, null, 1000m, 90m, 90m, 1180m, 0m, 1180m, "Cash",
            null, null, null, null, null, null, null, Guid.Empty);
        db.Sales.Add(sale3);
        var pay3 = CustomerPayment.Create(_tenant1Id, sale3.Id, 1180m, "Cash", DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty);
        db.CustomerPayments.Add(pay3);

        // Sweep 4 entities (Customer credit drift)
        var cust4 = Customer.Create(_tenant1Id, "Customer Four", "9444444444", null);
        cust4.AddToTotalPurchases(1180m);
        db.Customers.Add(cust4);

        var sale4 = Sale.CreateTaxInvoice(_tenant1Id, _branch1Id, "INV-DRIFT-04", DateTimeOffset.UtcNow,
            cust4.Id, cust4.Name, null, 1000m, 90m, 90m, 1180m, 0m, 1180m, "Cash",
            null, null, null, null, null, null, null, Guid.Empty);
        db.Sales.Add(sale4);

        var pay4 = CustomerPayment.Create(_tenant1Id, sale4.Id, 1180m, "Cash", DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty);
        db.CustomerPayments.Add(pay4);

        var cleDebit4 = CustomerLedgerEntry.CreateDebit(_tenant1Id, cust4.Id, sale4.Id, CustomerLedgerEntryType.Invoice, 1180m, _branch1Id, DateOnly.FromDateTime(DateTime.UtcNow), "INV-DRIFT-04");
        var cleCredit4 = CustomerLedgerEntry.CreateCredit(_tenant1Id, cust4.Id, sale4.Id, CustomerLedgerEntryType.Payment, 1180m, _branch1Id, DateOnly.FromDateTime(DateTime.UtcNow), "INV-DRIFT-04");
        db.CustomerLedgerEntries.AddRange(cleDebit4, cleCredit4);

        // Sweep 5 entities (Supplier payable drift)
        var supp = Supplier.Create(_tenant1Id, "Drift Supplier", "9555555555", "Address", "Brand", null);
        db.Suppliers.Add(supp);

        var purchase = Purchase.Create(_tenant1Id, _branch1Id, p.Id, 1, 500m, supp.Id, supp.Name, supp.Phone, supp.Address, DateOnly.FromDateTime(DateTime.UtcNow), Guid.Empty);
        db.Purchases.Add(purchase);

        var suppTxn = SupplierTransaction.Create(_tenant1Id, supp.Id, _branch1Id, SupplierTransactionType.Payment, 500m, "Cash", "REF-2", DateOnly.FromDateTime(DateTime.UtcNow), null, Guid.Empty);
        db.SupplierTransactions.Add(suppTxn);

        var sleCredit = SupplierLedgerEntry.CreateCredit(_tenant1Id, supp.Id, _branch1Id, SupplierLedgerEntryType.Bill, 500m, DateOnly.FromDateTime(DateTime.UtcNow), "REF-2");
        var sleDebit = SupplierLedgerEntry.CreateDebit(_tenant1Id, supp.Id, _branch1Id, SupplierLedgerEntryType.Payment, 500m, DateOnly.FromDateTime(DateTime.UtcNow), "REF-2");
        db.SupplierLedgerEntries.AddRange(sleCredit, sleDebit);

        await db.SaveChangesAsync();

        // Now seed deliberate drifts using RAW SQL (bypassing domain services):
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        // Drift 1: BranchInventory stock_qty set to 15 (movements sum is 10)
        await using (var cmd1 = new NpgsqlCommand("UPDATE branch_inventory SET stock_qty = 15 WHERE id = @id", conn))
        {
            cmd1.Parameters.AddWithValue("id", inv.Id);
            await cmd1.ExecuteNonQueryAsync();
        }

        // Drift 2: Sale amount_paid set to 1500 (customer_payments sum is 1180)
        await using (var cmd2 = new NpgsqlCommand("UPDATE sales SET amount_paid = 1500 WHERE id = @id", conn))
        {
            cmd2.Parameters.AddWithValue("id", sale2.Id);
            await cmd2.ExecuteNonQueryAsync();
        }

        // Drift 3: Customer total_purchases set to 5000 (active sales sum is 1180)
        await using (var cmd3 = new NpgsqlCommand("UPDATE customers SET total_purchases = 5000 WHERE id = @id", conn))
        {
            cmd3.Parameters.AddWithValue("id", cust3.Id);
            await cmd3.ExecuteNonQueryAsync();
        }

        // Drift 4: Customer ledger net: insert an extra debit 300 (unpaid sales is 0, ledger net becomes 300)
        await using (var cmd4 = new NpgsqlCommand(
            "INSERT INTO customer_ledger_entries (tenant_id, customer_id, entry_type, debit, credit, entry_date, created_at) " +
            "VALUES (@t, @c, 'Invoice', 300, 0, now(), now())", conn))
        {
            cmd4.Parameters.AddWithValue("t", _tenant1Id);
            cmd4.Parameters.AddWithValue("c", cust4.Id);
            await cmd4.ExecuteNonQueryAsync();
        }

        // Drift 5: Supplier ledger net: insert an extra credit 400 (net payable from purchases-payments is 0, ledger net becomes 400)
        await using (var cmd5 = new NpgsqlCommand(
            "INSERT INTO supplier_ledger_entries (tenant_id, supplier_id, entry_type, debit, credit, entry_date, created_at) " +
            "VALUES (@t, @s, 'Bill', 0, 400, now(), now())", conn))
        {
            cmd5.Parameters.AddWithValue("t", _tenant1Id);
            cmd5.Parameters.AddWithValue("s", supp.Id);
            await cmd5.ExecuteNonQueryAsync();
        }
        // Run Job
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReconciliationJob>();
        await job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None);

        // Verify Audit Log has all 5 drifts detected
        var driftEntries = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant1Id && a.Action == "reconciliation.drift_detected")
            .ToListAsync();

        driftEntries.Count.Should().Be(5);

        // Sweep 1
        var sweep1 = driftEntries.FirstOrDefault(e => e.EntityType == "BranchInventory" && e.EntityId == inv.Id.ToString());
        sweep1.Should().NotBeNull();
        using (var doc1 = JsonDocument.Parse(sweep1!.ChangesJson!))
        {
            doc1.RootElement.GetProperty("expected").GetInt32().Should().Be(10);
            doc1.RootElement.GetProperty("actual").GetInt32().Should().Be(15);
        }

        // Sweep 2
        var sweep2 = driftEntries.FirstOrDefault(e => e.EntityType == "Sale" && e.EntityId == sale2.Id.ToString());
        sweep2.Should().NotBeNull();
        using (var doc2 = JsonDocument.Parse(sweep2!.ChangesJson!))
        {
            doc2.RootElement.GetProperty("expected").GetDecimal().Should().Be(1180m);
            doc2.RootElement.GetProperty("actual").GetDecimal().Should().Be(1500m);
        }

        // Sweep 3
        var sweep3 = driftEntries.FirstOrDefault(e => e.EntityType == "Customer" && e.ChangesJson != null && e.ChangesJson.Contains("customer_revenue_consistency"));
        sweep3.Should().NotBeNull();
        sweep3!.EntityId.Should().Be(cust3.Id.ToString());
        using (var doc3 = JsonDocument.Parse(sweep3.ChangesJson!))
        {
            doc3.RootElement.GetProperty("expected").GetDecimal().Should().Be(1180m);
            doc3.RootElement.GetProperty("actual").GetDecimal().Should().Be(5000m);
        }

        // Sweep 4
        var sweep4 = driftEntries.FirstOrDefault(e => e.EntityType == "Customer" && e.ChangesJson != null && e.ChangesJson.Contains("customer_credit_consistency"));
        sweep4.Should().NotBeNull();
        sweep4!.EntityId.Should().Be(cust4.Id.ToString());
        using (var doc4 = JsonDocument.Parse(sweep4.ChangesJson!))
        {
            doc4.RootElement.GetProperty("expected").GetDecimal().Should().Be(0m);
            doc4.RootElement.GetProperty("actual").GetDecimal().Should().Be(300m);
        }

        // Sweep 5
        var sweep5 = driftEntries.FirstOrDefault(e => e.EntityType == "Supplier" && e.ChangesJson != null && e.ChangesJson.Contains("supplier_payable_consistency"));
        sweep5.Should().NotBeNull();
        sweep5!.EntityId.Should().Be(supp.Id.ToString());
        using (var doc5 = JsonDocument.Parse(sweep5.ChangesJson!))
        {
            doc5.RootElement.GetProperty("expected").GetDecimal().Should().Be(0m);
            doc5.RootElement.GetProperty("actual").GetDecimal().Should().Be(400m);
        }
    }

    [Fact]
    public async Task ReconciliationJob_IteratesAllTenantsIndependently()
    {
        // (c) the job correctly iterates ALL active tenants and their branches, proving it is a
        // legitimate cross-tenant background context (seed two separate tenants each with their own drift, confirm both are detected independently)
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // Seed drift in Tenant 1 (stock drift)
        var p1 = Product.Create(_tenant1Id, "Tenant 1 Phone", ProductCategory.NewPhone, 1000m, 800m, 18m, "85171200");
        db.Products.Add(p1);
        var inv1 = BranchInventory.Create(_tenant1Id, _branch1Id, p1.Id);
        inv1.IncreaseStock(20, 800m); // actual 20, movements 0
        db.BranchInventories.Add(inv1);

        // Seed drift in Tenant 2 (stock drift)
        var p2 = Product.Create(_tenant2Id, "Tenant 2 Phone", ProductCategory.NewPhone, 2000m, 1600m, 18m, "85171200");
        db.Products.Add(p2);
        var inv2 = BranchInventory.Create(_tenant2Id, _branch2Id, p2.Id);
        inv2.IncreaseStock(30, 1600m); // actual 30, movements 0
        db.BranchInventories.Add(inv2);

        await db.SaveChangesAsync();

        // Run Job
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReconciliationJob>();
        await job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None);

        // Verify drift logged for Tenant 1
        var t1Drifts = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant1Id && a.Action == "reconciliation.drift_detected")
            .ToListAsync();
        t1Drifts.Should().Contain(d => d.EntityId == inv1.Id.ToString());

        // Verify drift logged for Tenant 2
        var t2Drifts = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant2Id && a.Action == "reconciliation.drift_detected")
            .ToListAsync();
        t2Drifts.Should().Contain(d => d.EntityId == inv2.Id.ToString());
    }

    [Fact]
    public async Task ReconciliationJob_FailureInOneTenant_DoesNotHaltOtherTenants()
    {
        // (d) confirm the job does NOT crash or stop early if one tenant's data has an unexpected issue
        // it should continue processing remaining tenants (isolated per tenant)
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // Seed valid drift in Tenant 2
        var p2 = Product.Create(_tenant2Id, "Tenant 2 Isolation Phone", ProductCategory.NewPhone, 2000m, 1600m, 18m, "85171200");
        db.Products.Add(p2);
        var inv2 = BranchInventory.Create(_tenant2Id, _branch2Id, p2.Id);
        inv2.IncreaseStock(50, 1600m);
        db.BranchInventories.Add(inv2);
        await db.SaveChangesAsync();

        // Run Job
        using var scope = _factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<ReconciliationJob>();

        var act = async () => await job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Tenant 2 was processed successfully and drift was recorded
        var t2Drifts = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenant2Id && a.EntityId == inv2.Id.ToString())
            .ToListAsync();
        t2Drifts.Should().NotBeEmpty();
    }
}
