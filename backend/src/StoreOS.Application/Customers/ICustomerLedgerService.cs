namespace StoreOS.Application.Customers;

public interface ICustomerLedgerService
{
    Task RecordInvoiceDebitAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task RecordPaymentCreditAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task RecordVoidReversalAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task RecordRefundCreditAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task<decimal> GetConsolidatedBalanceAsync(Guid customerId, CancellationToken ct);
}
