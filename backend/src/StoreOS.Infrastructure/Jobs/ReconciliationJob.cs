using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Jobs;

public class ReconciliationJob
{
    private static readonly Meter ReconciliationMeter = new("StoreOS.Reconciliation", "1.0.0");
    private static readonly Counter<int> DriftCounter = ReconciliationMeter.CreateCounter<int>(
        "reconciliation_drift_total", "count", "Total reconciliation drift events detected");

    private readonly StoreOsDbContext _db;
    private readonly ITenantJobRunner _jobRunner;
    private readonly ILogger<ReconciliationJob> _logger;

    public ReconciliationJob(
        StoreOsDbContext db,
        ITenantJobRunner jobRunner,
        ILogger<ReconciliationJob> logger)
    {
        _db = db;
        _jobRunner = jobRunner;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().Where(t => t.IsActive).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            try
            {
                await _jobRunner.RunAsync(tenant.Id, async (sp, token) =>
                {
                    var scopedDb = sp.GetRequiredService<StoreOsDbContext>();
                    var audit = sp.GetRequiredService<IAuditWriter>();

                    var branches = await scopedDb.Branches.IgnoreQueryFilters()
                        .Where(b => b.TenantId == tenant.Id && b.IsActive)
                        .ToListAsync(token);

                    foreach (var branch in branches)
                    {
                        await ReconcileStockAsync(tenant.Id, branch.Id, scopedDb, audit, token); // Sweep 1
                    }

                    await ReconcileSalePaymentsAsync(tenant.Id, scopedDb, audit, token);    // Sweep 2
                    await ReconcileCustomerRevenueAsync(tenant.Id, scopedDb, audit, token); // Sweep 3
                    await ReconcileCustomerCreditAsync(tenant.Id, scopedDb, audit, token);  // Sweep 4
                    await ReconcileSupplierPayablesAsync(tenant.Id, scopedDb, audit, token);// Sweep 5

                    await scopedDb.SaveChangesAsync(token);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation failed for tenant {TenantId}. Continuing with remaining tenants.", tenant.Id);
            }
        }
    }

    public async Task ReconcileStockAsync(Guid tenantId, Guid branchId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        // Sweep 1: branch_inventory.stock_qty == SUM(inventory_movements.qty_delta) per product/branch
        var inventories = await db.BranchInventories.IgnoreQueryFilters()
            .Where(bi => bi.TenantId == tenantId && bi.BranchId == branchId)
            .ToListAsync(ct);

        var movementSums = await db.InventoryMovements.IgnoreQueryFilters()
            .Where(im => im.TenantId == tenantId && im.BranchId == branchId)
            .GroupBy(im => im.ProductId)
            .Select(g => new { ProductId = g.Key, TotalDelta = g.Sum(im => im.QtyDelta) })
            .ToDictionaryAsync(x => x.ProductId, x => x.TotalDelta, ct);

        foreach (var inv in inventories)
        {
            var expectedStock = movementSums.TryGetValue(inv.ProductId, out var sum) ? sum : 0;
            if (inv.StockQty != expectedStock)
            {
                DriftCounter.Add(1,
                    new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()),
                    new KeyValuePair<string, object?>("sweep", "stock_consistency"));

                await audit.WriteAsync(branchId, "reconciliation.drift_detected", "BranchInventory", inv.Id.ToString(),
                    new
                    {
                        sweep = "stock_consistency",
                        productId = inv.ProductId,
                        branchId = inv.BranchId,
                        expected = expectedStock,
                        actual = inv.StockQty
                    }, ct);
            }
        }
    }

    public async Task ReconcileSalePaymentsAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        // Sweep 2: sales.amount_paid == SUM(customer_payments.amount) per sale
        var sales = await db.Sales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);

        var paymentSums = await db.CustomerPayments.IgnoreQueryFilters()
            .Where(cp => cp.TenantId == tenantId)
            .GroupBy(cp => cp.SaleId)
            .Select(g => new { SaleId = g.Key, TotalPaid = g.Sum(cp => cp.Amount) })
            .ToDictionaryAsync(x => x.SaleId, x => x.TotalPaid, ct);

        foreach (var sale in sales)
        {
            var expectedAmountPaid = paymentSums.TryGetValue(sale.Id, out var sum) ? sum : 0m;
            if (sale.AmountPaid != expectedAmountPaid)
            {
                DriftCounter.Add(1,
                    new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()),
                    new KeyValuePair<string, object?>("sweep", "sale_payment_consistency"));

                await audit.WriteAsync(sale.BranchId, "reconciliation.drift_detected", "Sale", sale.Id.ToString(),
                    new
                    {
                        sweep = "sale_payment_consistency",
                        saleId = sale.Id,
                        expected = expectedAmountPaid,
                        actual = sale.AmountPaid
                    }, ct);
            }
        }
    }

    public async Task ReconcileCustomerRevenueAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        // Sweep 3: customers.total_purchases == SUM(sales.grand_total WHERE status='Active') per customer
        var customers = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        var activeSalesSums = await db.Sales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.CustomerId != null && s.Status == SaleStatus.Active)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, TotalRevenue = g.Sum(s => s.GrandTotal) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.TotalRevenue, ct);

        foreach (var customer in customers)
        {
            var expectedRevenue = activeSalesSums.TryGetValue(customer.Id, out var sum) ? sum : 0m;
            if (customer.TotalPurchases != expectedRevenue)
            {
                DriftCounter.Add(1,
                    new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()),
                    new KeyValuePair<string, object?>("sweep", "customer_revenue_consistency"));

                await audit.WriteAsync(null, "reconciliation.drift_detected", "Customer", customer.Id.ToString(),
                    new
                    {
                        sweep = "customer_revenue_consistency",
                        customerId = customer.Id,
                        expected = expectedRevenue,
                        actual = customer.TotalPurchases
                    }, ct);
            }
        }
    }

    public async Task ReconcileCustomerCreditAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        // Sweep 4: derived customer credit == SUM(customer_ledger_entries.debit - credit) per customer
        // Unpaid sales balance derived from sales: SUM(sales.grand_total - sales.amount_paid) WHERE status = 'Active'
        // Compared against ledger net: SUM(customer_ledger_entries.debit - customer_ledger_entries.credit)
        var customers = await db.Customers.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .ToListAsync(ct);

        var salesUnpaidSums = await db.Sales.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && s.CustomerId != null && s.Status == SaleStatus.Active)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, UnpaidSales = g.Sum(s => s.GrandTotal - s.AmountPaid) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.UnpaidSales, ct);

        var ledgerSums = await db.CustomerLedgerEntries.IgnoreQueryFilters()
            .Where(cle => cle.TenantId == tenantId)
            .GroupBy(cle => cle.CustomerId)
            .Select(g => new { CustomerId = g.Key, NetBalance = g.Sum(cle => cle.Debit - cle.Credit) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.NetBalance, ct);

        foreach (var customer in customers)
        {
            var expectedCredit = salesUnpaidSums.TryGetValue(customer.Id, out var s) ? s : 0m;
            var actualLedgerCredit = ledgerSums.TryGetValue(customer.Id, out var l) ? l : 0m;

            if (expectedCredit != actualLedgerCredit)
            {
                DriftCounter.Add(1,
                    new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()),
                    new KeyValuePair<string, object?>("sweep", "customer_credit_consistency"));

                await audit.WriteAsync(null, "reconciliation.drift_detected", "Customer", customer.Id.ToString(),
                    new
                    {
                        sweep = "customer_credit_consistency",
                        customerId = customer.Id,
                        expected = expectedCredit,
                        actual = actualLedgerCredit
                    }, ct);
            }
        }
    }

    public async Task ReconcileSupplierPayablesAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        // Sweep 5: derived supplier payable == SUM(supplier_ledger_entries.credit - debit) per supplier
        // Net payable derived from purchases minus payments
        var suppliers = await db.Suppliers.IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(ct);

        var purchasesSums = await db.Purchases.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.SupplierId != null)
            .GroupBy(p => p.SupplierId!.Value)
            .Select(g => new { SupplierId = g.Key, TotalPurchases = g.Sum(p => p.Qty * p.PurchaseRate) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.TotalPurchases, ct);

        var paymentSums = await db.SupplierTransactions.IgnoreQueryFilters()
            .Where(st => st.TenantId == tenantId && st.Type == SupplierTransactionType.Payment)
            .GroupBy(st => st.SupplierId)
            .Select(g => new { SupplierId = g.Key, TotalPayments = g.Sum(st => st.Amount) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.TotalPayments, ct);

        var ledgerSums = await db.SupplierLedgerEntries.IgnoreQueryFilters()
            .Where(sle => sle.TenantId == tenantId)
            .GroupBy(sle => sle.SupplierId)
            .Select(g => new { SupplierId = g.Key, NetPayable = g.Sum(sle => sle.Credit - sle.Debit) })
            .ToDictionaryAsync(x => x.SupplierId, x => x.NetPayable, ct);

        foreach (var supplier in suppliers)
        {
            var totalPurchased = purchasesSums.TryGetValue(supplier.Id, out var pur) ? pur : 0m;
            var totalPaid = paymentSums.TryGetValue(supplier.Id, out var pay) ? pay : 0m;
            var expectedPayable = Math.Max(0m, totalPurchased - totalPaid);

            var actualLedgerPayable = ledgerSums.TryGetValue(supplier.Id, out var l) ? l : 0m;

            if (expectedPayable != actualLedgerPayable)
            {
                DriftCounter.Add(1,
                    new KeyValuePair<string, object?>("tenant_id", tenantId.ToString()),
                    new KeyValuePair<string, object?>("sweep", "supplier_payable_consistency"));

                await audit.WriteAsync(null, "reconciliation.drift_detected", "Supplier", supplier.Id.ToString(),
                    new
                    {
                        sweep = "supplier_payable_consistency",
                        supplierId = supplier.Id,
                        expected = expectedPayable,
                        actual = actualLedgerPayable
                    }, ct);
            }
        }
    }
}
