using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.FinanceOps;

public class LiabilityPayment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LiabilityId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMode { get; private set; } = default!;
    public DateOnly PaymentDate { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private LiabilityPayment() { }

    public static LiabilityPayment Create(
        Guid tenantId,
        Guid liabilityId,
        decimal amount,
        string paymentMode,
        DateOnly paymentDate,
        string? notes)
    {
        if (amount <= 0)
            throw new DomainValidationException("invalid_payment_amount", "Payment amount must be positive.");

        if (string.IsNullOrWhiteSpace(paymentMode))
            throw new DomainValidationException("payment_mode_required", "Payment mode is required.");

        return new LiabilityPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            LiabilityId = liabilityId,
            Amount = amount,
            PaymentMode = paymentMode.Trim(),
            PaymentDate = paymentDate,
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static LiabilityPayment Rehydrate(
        Guid id, Guid tenantId, Guid liabilityId, decimal amount,
        string paymentMode, DateOnly paymentDate, string? notes, DateTimeOffset createdAt)
    {
        return new LiabilityPayment
        {
            Id = id,
            TenantId = tenantId,
            LiabilityId = liabilityId,
            Amount = amount,
            PaymentMode = paymentMode,
            PaymentDate = paymentDate,
            Notes = notes,
            CreatedAt = createdAt
        };
    }
}
