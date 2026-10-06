using System.Reflection;
using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Domain.Tests.FinanceOps;

public class LiabilityPaymentTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _liabilityId = Guid.NewGuid();

    [Fact]
    public void LiabilityPayment_Create_Valid_ShouldSucceed()
    {
        var payment = LiabilityPayment.Create(
            _tenantId,
            _liabilityId,
            25000m,
            "BankTransfer",
            new DateOnly(2026, 10, 7),
            "EMI Payment #1");

        payment.TenantId.Should().Be(_tenantId);
        payment.LiabilityId.Should().Be(_liabilityId);
        payment.Amount.Should().Be(25000m);
        payment.PaymentMode.Should().Be("BankTransfer");
        payment.PaymentDate.Should().Be(new DateOnly(2026, 10, 7));
        payment.Notes.Should().Be("EMI Payment #1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void LiabilityPayment_Create_NonPositiveAmount_ShouldThrowDomainValidationException(decimal invalidAmount)
    {
        Action act = () => LiabilityPayment.Create(
            _tenantId,
            _liabilityId,
            invalidAmount,
            "Cash",
            new DateOnly(2026, 10, 7),
            null);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "invalid_payment_amount");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void LiabilityPayment_Create_EmptyPaymentMode_ShouldThrowDomainValidationException(string? invalidMode)
    {
        Action act = () => LiabilityPayment.Create(
            _tenantId,
            _liabilityId,
            1000m,
            invalidMode!,
            new DateOnly(2026, 10, 7),
            null);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "payment_mode_required");
    }

    [Fact]
    public void LiabilityPayment_IsPermanentlyImmutable_NoUpdateMethodExists()
    {
        // Requirements & Invariants: LiabilityPayment is strictly immutable, mirroring CustomerPayment (P-508)
        var methods = typeof(LiabilityPayment)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName) // Exclude property getters/setters
            .ToList();

        // Confirm by code inspection and reflection that no mutating update methods exist
        methods.Should().BeEmpty("LiabilityPayment must be strictly immutable with no public instance mutating methods");
    }
}
