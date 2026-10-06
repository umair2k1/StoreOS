using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Suppliers;
using StoreOS.Domain.Suppliers;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Suppliers;

public class SupplierLedgerService : ISupplierLedgerService
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;

    public SupplierLedgerService(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task RecordBillAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct)
    {
        var entry = SupplierLedgerEntry.CreateCredit(_scope.TenantId, supplierId, branchId,
            SupplierLedgerEntryType.Bill, amount, entryDate, refId);
        await _db.Set<SupplierLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordPaymentAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct)
    {
        var entry = SupplierLedgerEntry.CreateDebit(_scope.TenantId, supplierId, branchId,
            SupplierLedgerEntryType.Payment, amount, entryDate, refId);
        await _db.Set<SupplierLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordOpeningBalanceAsync(Guid supplierId, decimal amount, DateOnly entryDate, CancellationToken ct)
    {
        var entry = SupplierLedgerEntry.CreateCredit(_scope.TenantId, supplierId, null,
            SupplierLedgerEntryType.OpeningBalance, amount, entryDate, null);
        await _db.Set<SupplierLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordReturnCreditAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct)
    {
        var entry = SupplierLedgerEntry.CreateDebit(_scope.TenantId, supplierId, branchId,
            SupplierLedgerEntryType.ReturnCredit, amount, entryDate, refId);
        await _db.Set<SupplierLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task<decimal> GetPayableAsync(Guid supplierId, CancellationToken ct)
    {
        var sums = await _db.Set<SupplierLedgerEntry>().IgnoreQueryFilters()
            .Where(e => e.TenantId == _scope.TenantId && e.SupplierId == supplierId)
            .GroupBy(e => 1)
            .Select(g => new { Debit = g.Sum(e => e.Debit), Credit = g.Sum(e => e.Credit) })
            .FirstOrDefaultAsync(ct);

        if (sums is null) return 0m;
        return Math.Max(0m, sums.Credit - sums.Debit);
    }
}
