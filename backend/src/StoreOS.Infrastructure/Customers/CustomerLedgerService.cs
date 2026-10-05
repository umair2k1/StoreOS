using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Domain.Customers;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Customers;

public class CustomerLedgerService : ICustomerLedgerService
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;

    public CustomerLedgerService(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task RecordInvoiceDebitAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateDebit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.Invoice, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordPaymentCreditAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateCredit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.Payment, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordVoidReversalAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateCredit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.InvoiceVoid, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordRefundCreditAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateCredit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.Refund, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task<decimal> GetConsolidatedBalanceAsync(Guid customerId, CancellationToken ct)
    {
        // Tenant-global by design (Requirements §17.5) — deliberately bypasses branch query
        // filters, but stays strictly scoped to the caller's OWN tenant via the explicit
        // tenant_id predicate below. See P-506's documented two-query-pattern rule.
        var sums = await _db.Set<CustomerLedgerEntry>().IgnoreQueryFilters()
            .Where(e => e.TenantId == _scope.TenantId && e.CustomerId == customerId)
            .GroupBy(e => 1)
            .Select(g => new { Debit = g.Sum(e => e.Debit), Credit = g.Sum(e => e.Credit) })
            .FirstOrDefaultAsync(ct);

        if (sums is null) return 0m;
        return Math.Max(0m, sums.Debit - sums.Credit);
    }
}
