using FluentAssertions;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Sales;

namespace StoreOS.Domain.Tests.Sales;

public class PaymentComposerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _saleId = Guid.NewGuid();
    private readonly Guid _receivedBy = Guid.NewGuid();

    [Fact]
    public void Compose_TwoPayments_ReturnsCorrectEntitiesAndSum()
    {
        var requested = new List<CheckoutPayment>
        {
            new CheckoutPayment(PaymentMode.Cash, 3000m),
            new CheckoutPayment(PaymentMode.UPI, 2000m)
        };
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var (payments, totalPaid) = PaymentComposer.Compose(
            _tenantId, _saleId, requested, date, _receivedBy);

        totalPaid.Should().Be(5000m);
        payments.Should().HaveCount(2);

        payments[0].PaymentMode.Should().Be(PaymentMode.Cash);
        payments[0].Amount.Should().Be(3000m);
        payments[0].SaleId.Should().Be(_saleId);
        payments[0].TenantId.Should().Be(_tenantId);

        payments[1].PaymentMode.Should().Be(PaymentMode.UPI);
        payments[1].Amount.Should().Be(2000m);
        payments[1].SaleId.Should().Be(_saleId);
        payments[1].TenantId.Should().Be(_tenantId);
    }

    [Fact]
    public void Compose_EmptyPayments_ReturnsZeroTotalAndEmptyList()
    {
        var requested = new List<CheckoutPayment>();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        var (payments, totalPaid) = PaymentComposer.Compose(
            _tenantId, _saleId, requested, date, _receivedBy);

        totalPaid.Should().Be(0m);
        payments.Should().BeEmpty();
    }
}
