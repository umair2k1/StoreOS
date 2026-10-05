using FluentAssertions;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Sales;

namespace StoreOS.IntegrationTests.Sales;

public class CheckoutValidatorTests
{
    private readonly CheckoutValidator _validator = new();

    private CheckoutCommand CreateValidCommand()
    {
        return new CheckoutCommand(
            Lines: new List<CheckoutCartLine>
            {
                new CheckoutCartLine(
                    ProductId: Guid.NewGuid(),
                    Qty: 1,
                    Imei: null,
                    UseMarginScheme: false,
                    IsGift: false,
                    OverridePricePerUnit: null)
            },
            CustomerId: null,
            WalkInCustomerName: "Rajesh Kumar",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment>
            {
                new CheckoutPayment(PaymentMode.Cash, 1000m)
            },
            Finance: null,
            Description: "Valid checkout"
        );
    }

    [Fact]
    public void Validate_EmptyLines_ShouldFailValidation()
    {
        var command = CreateValidCommand() with { Lines = new List<CheckoutCartLine>() };
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Lines");
    }

    [Fact]
    public void Validate_LineWithZeroQty_ShouldFailValidation()
    {
        var command = CreateValidCommand() with
        {
            Lines = new List<CheckoutCartLine>
            {
                new CheckoutCartLine(Guid.NewGuid(), 0, null, false, false, null)
            }
        };
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Lines[0].Qty"));
    }

    [Fact]
    public void Validate_NegativeInvoiceDiscount_ShouldFailValidation()
    {
        var command = CreateValidCommand() with { InvoiceDiscount = -10m };
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "InvoiceDiscount");
    }

    [Fact]
    public void Validate_BogusInvoiceType_ShouldFailValidation()
    {
        var command = CreateValidCommand() with { InvoiceType = "Bogus" };
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "InvoiceType");
    }

    [Fact]
    public void Validate_InvalidPaymentMode_ShouldFailValidation()
    {
        var command = CreateValidCommand() with
        {
            Payments = new List<CheckoutPayment>
            {
                new CheckoutPayment("Bitcoin", 500m)
            }
        };
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Payments[0].PaymentMode"));
    }

    [Fact]
    public void Validate_FinanceWithZeroTenureMonths_ShouldFailValidation()
    {
        var command = CreateValidCommand() with
        {
            Finance = new CheckoutFinanceDetails(
                FinanceProvider: "Bajaj",
                FinancerName: "Bajaj Finserv",
                DownPayment: 1000m,
                TenureMonths: 0,
                AnnualInterestRate: 12m)
        };
        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.StartsWith("Finance.TenureMonths"));
    }

    [Fact]
    public void Validate_WellFormedCommand_ShouldPassValidation()
    {
        var command = CreateValidCommand();
        var result = _validator.Validate(command);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}
