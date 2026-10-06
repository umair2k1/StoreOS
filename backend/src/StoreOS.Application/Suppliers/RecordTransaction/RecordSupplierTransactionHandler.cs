using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.RecordTransaction;

public class RecordSupplierTransactionHandler : IRequestHandler<RecordSupplierTransactionCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly ISupplierLedgerService _ledgerService;

    public RecordSupplierTransactionHandler(
        IAppDbContext db,
        IAccessScope scope,
        IBranchContext branchContext,
        ISupplierLedgerService ledgerService)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _ledgerService = ledgerService;
    }

    public async Task<Guid> Handle(RecordSupplierTransactionCommand request, CancellationToken ct)
    {
        var supplierExists = await _db.Set<Supplier>()
            .AnyAsync(s => s.Id == request.SupplierId, ct);
        if (!supplierExists)
        {
            throw new DomainException("supplier_not_found", $"Supplier '{request.SupplierId}' was not found.");
        }

        var txnDate = request.TxnDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : request.TxnDate;

        var transaction = SupplierTransaction.Create(
            _scope.TenantId,
            request.SupplierId,
            _branchContext.CurrentBranchId,
            request.Type,
            request.Amount,
            request.PaymentMode,
            request.ReferenceNo,
            txnDate,
            request.Notes,
            _scope.UserId);

        await _db.Set<SupplierTransaction>().AddAsync(transaction, ct);

        switch (request.Type)
        {
            case SupplierTransactionType.Bill:
                await _ledgerService.RecordBillAsync(
                    request.SupplierId,
                    request.Amount,
                    _branchContext.CurrentBranchId,
                    txnDate,
                    request.ReferenceNo ?? transaction.Id.ToString(),
                    ct);
                break;
            case SupplierTransactionType.Payment:
                await _ledgerService.RecordPaymentAsync(
                    request.SupplierId,
                    request.Amount,
                    _branchContext.CurrentBranchId,
                    txnDate,
                    request.ReferenceNo ?? transaction.Id.ToString(),
                    ct);
                break;
            case SupplierTransactionType.OpeningBalance:
                await _ledgerService.RecordOpeningBalanceAsync(
                    request.SupplierId,
                    request.Amount,
                    txnDate,
                    ct);
                break;
            default:
                throw new DomainValidationException("invalid_transaction_type", $"Unknown transaction type '{request.Type}'.");
        }

        await _db.SaveChangesAsync(ct);

        return transaction.Id;
    }
}
