using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Sales;

public class CustomerPayment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMode { get; private set; } = default!;
    public DateOnly PaymentDate { get; private set; }
    public Guid ReceivedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomerPayment() { }

    public static CustomerPayment Create(Guid tenantId, Guid saleId, decimal amount,
        string paymentMode, DateOnly paymentDate, Guid receivedBy)
    {
        if (amount <= 0) throw new DomainValidationException("invalid_payment_amount", "Payment amount must be positive.");
        if (!Sales.PaymentMode.All.Contains(paymentMode))
            throw new DomainValidationException("invalid_payment_mode", $"Unknown payment mode '{paymentMode}'.");
        return new CustomerPayment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SaleId = saleId,
            Amount = amount,
            PaymentMode = paymentMode,
            PaymentDate = paymentDate,
            ReceivedBy = receivedBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
