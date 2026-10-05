namespace StoreOS.Application.Sales.Checkout;

public static class PaymentComposer
{
    public static (IReadOnlyList<StoreOS.Domain.Sales.CustomerPayment> Payments, decimal TotalPaid) Compose(
        Guid tenantId, Guid saleId, IReadOnlyList<CheckoutPayment> requested, DateOnly paymentDate, Guid receivedBy)
    {
        var payments = requested.Select(p =>
            StoreOS.Domain.Sales.CustomerPayment.Create(tenantId, saleId, p.Amount, p.PaymentMode, paymentDate, receivedBy))
            .ToList();
        var total = payments.Sum(p => p.Amount);
        return (payments, total);
    }
}
