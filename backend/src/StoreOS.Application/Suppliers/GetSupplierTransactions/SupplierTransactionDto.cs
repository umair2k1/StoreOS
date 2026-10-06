namespace StoreOS.Application.Suppliers.GetSupplierTransactions;

public record SupplierTransactionDto(
    Guid Id,
    Guid SupplierId,
    Guid? BranchId,
    string Type,
    decimal Amount,
    string? PaymentMode,
    string? ReferenceNo,
    DateOnly TxnDate,
    string? Notes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);
