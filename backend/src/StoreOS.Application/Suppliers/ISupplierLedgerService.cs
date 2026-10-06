namespace StoreOS.Application.Suppliers;

public interface ISupplierLedgerService
{
    Task RecordBillAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
    Task RecordPaymentAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
    Task RecordOpeningBalanceAsync(Guid supplierId, decimal amount, DateOnly entryDate, CancellationToken ct);
    Task RecordReturnCreditAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
    Task<decimal> GetPayableAsync(Guid supplierId, CancellationToken ct);
}
