using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Finance;

namespace StoreOS.Domain.Tests.Finance;

public class FinanceEngineTests
{
    [Fact]
    public void Case1_CalculateEmi_ZeroInterest_ComputesExactPrincipalDividedByMonths()
    {
        // Arithmetic:
        // principal = 12000m, annualRatePercent = 0m, months = 12
        // emi = Money.Round2(12000 / 12) = 1000.00m
        var emi = FinanceEngine.CalculateEmi(principal: 12000m, annualRatePercent: 0m, months: 12);

        emi.Should().Be(1000.00m);
    }

    [Fact]
    public void Case2_CalculateEmi_PositiveInterest_ComputesExactEmiWithDecimalPow()
    {
        // Arithmetic:
        // principal = 12000m, annualRatePercent = 12m, months = 12
        // r = annualRatePercent / 12 / 100 = 12 / 12 / 100 = 0.01m
        // 1 + r = 1.01m
        // DecimalPow(1.01m, 12) = 1.01^12
        // Exact decimal product: 1.01^12 = 1.126825030131969720661201m (24 decimal places)
        // emi = principal * r * pow / (pow - 1)
        //     = 12000 * 0.01 * 1.126825030131969720661201 / (1.126825030131969720661201 - 1)
        //     = 135.21900361583636647934412 / 0.126825030131969720661201
        //     = 1066.185464140100488079853975m
        // Money.Round2(emi) = 1066.19m (away-from-zero round)
        var emi = FinanceEngine.CalculateEmi(principal: 12000m, annualRatePercent: 12m, months: 12);

        emi.Should().Be(1066.19m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Case3_CalculateEmi_InvalidTenure_ThrowsDomainValidationException(int months)
    {
        var act = () => FinanceEngine.CalculateEmi(principal: 12000m, annualRatePercent: 10m, months: months);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("finance_invalid_tenure");
        ex.Message.Should().Contain("Tenure in months must be greater than zero.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Case4_CalculateEmi_ZeroOrNegativePrincipal_ThrowsDomainValidationException(decimal principal)
    {
        var act = () => FinanceEngine.CalculateEmi(principal: principal, annualRatePercent: 10m, months: 12);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("finance_principal_not_positive");
        ex.Message.Should().Contain("Financed principal must be greater than zero.");
    }

    [Fact]
    public void Case5_CalculateEmi_NegativeRate_ThrowsDomainValidationException()
    {
        var act = () => FinanceEngine.CalculateEmi(principal: 10000m, annualRatePercent: -5m, months: 12);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("finance_invalid_rate");
        ex.Message.Should().Contain("Annual interest rate cannot be negative.");
    }

    [Fact]
    public void Case6_CalculatePrincipal_DownPaymentExceedsTotal_ThrowsDomainValidationException()
    {
        var act = () => FinanceEngine.CalculatePrincipal(grandTotal: 10000m, downPayment: 15000m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("finance_down_payment_exceeds_total");
        ex.Message.Should().Contain("Down payment cannot exceed grand total.");
    }

    [Fact]
    public void Case7_CalculatePrincipal_DownPaymentEqualsTotalExactly_ThrowsDomainValidationException()
    {
        // When down payment equals grand total, financed principal is 0m, which is invalid for financing.
        var act = () => FinanceEngine.CalculatePrincipal(grandTotal: 10000m, downPayment: 10000m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("finance_principal_not_positive");
        ex.Message.Should().Contain("Financed principal must be greater than zero.");
    }

    [Fact]
    public void Case8_CalculatePrincipal_NegativeDownPayment_ThrowsDomainValidationException()
    {
        var act = () => FinanceEngine.CalculatePrincipal(grandTotal: 10000m, downPayment: -100m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("finance_down_payment_negative");
        ex.Message.Should().Contain("Down payment cannot be negative.");
    }
}
