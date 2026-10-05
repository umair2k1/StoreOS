using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Domain.Tests.Sales;

public class CustomerPaymentTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _saleId = Guid.NewGuid();
    private readonly Guid _receivedBy = Guid.NewGuid();

    [Fact]
    public void Create_ValidPayment_ShouldSucceed()
    {
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var payment = CustomerPayment.Create(
            _tenantId, _saleId, 2500m, PaymentMode.UPI, paymentDate, _receivedBy);

        payment.TenantId.Should().Be(_tenantId);
        payment.SaleId.Should().Be(_saleId);
        payment.Amount.Should().Be(2500m);
        payment.PaymentMode.Should().Be(PaymentMode.UPI);
        payment.PaymentDate.Should().Be(paymentDate);
        payment.ReceivedBy.Should().Be(_receivedBy);
        payment.Id.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void Create_NonPositiveAmount_ShouldThrowInvalidPaymentAmount(decimal invalidAmount)
    {
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => CustomerPayment.Create(
            _tenantId, _saleId, invalidAmount, PaymentMode.Cash, paymentDate, _receivedBy);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_payment_amount");
    }

    [Fact]
    public void Create_InvalidPaymentMode_ShouldThrowInvalidPaymentMode()
    {
        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => CustomerPayment.Create(
            _tenantId, _saleId, 1000m, "Cryptocurrency", paymentDate, _receivedBy);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_payment_mode");
    }

    [Fact]
    public void CustomerPayment_ExposesNoPublicMutationMethods_IsPermanentlyImmutable()
    {
        // Enforce Invariant 5: CustomerPayment is permanent and immutable.
        // It exposes only static factory 'Create' and read-only property getters.
        var methods = typeof(CustomerPayment).GetMethods(
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly);

        // All declared public instance methods must be property getters (special name get_*)
        var nonGetterMethods = methods.Where(m => !m.IsSpecialName || !m.Name.StartsWith("get_")).ToList();
        nonGetterMethods.Should().BeEmpty("CustomerPayment must be completely immutable with no mutation methods");
    }
}
