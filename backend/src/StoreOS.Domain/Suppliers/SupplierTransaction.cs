using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Suppliers;

public class SupplierTransaction
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Type { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string? PaymentMode { get; private set; }
    public string? ReferenceNo { get; private set; }
    public DateOnly TxnDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SupplierTransaction() { }

    public static SupplierTransaction Create(
        Guid tenantId,
        Guid supplierId,
        Guid? branchId,
        string type,
        decimal amount,
        string? paymentMode,
        string? referenceNo,
        DateOnly txnDate,
        string? notes,
        Guid createdBy)
    {
        if (amount <= 0)
            throw new DomainValidationException("invalid_amount", "Transaction amount must be positive.");

        if (!SupplierTransactionType.IsValid(type))
            throw new DomainValidationException("invalid_transaction_type", $"Unknown transaction type '{type}'.");

        if (type == SupplierTransactionType.Payment)
        {
            if (string.IsNullOrWhiteSpace(paymentMode) || !SupplierPaymentMode.IsValid(paymentMode))
                throw new DomainValidationException("invalid_payment_mode", "Payment mode is required and must be valid for Payment transactions.");
        }
        else if (paymentMode != null && !SupplierPaymentMode.IsValid(paymentMode))
        {
            throw new DomainValidationException("invalid_payment_mode", $"Unknown payment mode '{paymentMode}'.");
        }

        return new SupplierTransaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SupplierId = supplierId,
            BranchId = branchId,
            Type = type,
            Amount = amount,
            PaymentMode = paymentMode,
            ReferenceNo = referenceNo,
            TxnDate = txnDate,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
