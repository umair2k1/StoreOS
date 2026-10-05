using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tax;

namespace StoreOS.Domain.Tests.Tax;

public class MarginSchemeGstTests
{
    [Fact]
    public void Case1_MarginSchemeGst_StandardMargin_ComputesTaxableAndGstHalves_WithHalfUpRounding()
    {
        // Arithmetic:
        // sell = 15000m, cost = 12000m, gstRate = 18m
        // margin = sell - cost = 15000 - 12000 = 3000m
        // taxable = Round2(3000 / (1 + 18 / 100)) = Round2(3000 / 1.18)
        //         = Round2(2542.3728813559322033898305085m) = 2542.37m
        // totalGst = Round2(margin - taxable) = Round2(3000 - 2542.37) = 457.63m
        // cgst = Round2(totalGst / 2) = Round2(457.63 / 2) = Round2(228.815m)
        //      = 228.82m (MidpointRounding.AwayFromZero rounds .815 up to .82 at 2nd decimal boundary)
        // sgst = Round2(totalGst - cgst) = Round2(457.63 - 228.82) = 228.81m
        // TotalGst = cgst + sgst = 228.82 + 228.81 = 457.63m
        var result = TaxEngine.MarginSchemeGst(sell: 15000m, cost: 12000m, gstRate: 18m);

        result.Taxable.Should().Be(2542.37m);
        result.Cgst.Should().Be(228.82m);
        result.Sgst.Should().Be(228.81m);
        result.TotalGst.Should().Be(457.63m);
    }

    [Fact]
    public void Case2_MarginSchemeGst_ZeroMargin_ReturnsLineTaxZero()
    {
        // Arithmetic:
        // sell = 10000m, cost = 10000m, gstRate = 18m
        // margin = sell - cost = 10000 - 10000 = 0m
        // margin <= 0 branch triggers -> LineTax.Zero
        var result = TaxEngine.MarginSchemeGst(sell: 10000m, cost: 10000m, gstRate: 18m);

        result.Should().Be(LineTax.Zero);
        result.Taxable.Should().Be(0m);
        result.Cgst.Should().Be(0m);
        result.Sgst.Should().Be(0m);
        result.TotalGst.Should().Be(0m);
    }

    [Fact]
    public void Case3_MarginSchemeGst_NegativeMargin_ReturnsLineTaxZero()
    {
        // Arithmetic:
        // sell = 9000m, cost = 10000m, gstRate = 18m
        // margin = sell - cost = 9000 - 10000 = -1000m
        // margin <= 0 branch triggers -> LineTax.Zero
        var result = TaxEngine.MarginSchemeGst(sell: 9000m, cost: 10000m, gstRate: 18m);

        result.Should().Be(LineTax.Zero);
        result.Taxable.Should().Be(0m);
        result.Cgst.Should().Be(0m);
        result.Sgst.Should().Be(0m);
        result.TotalGst.Should().Be(0m);
    }

    [Fact]
    public void Case4_MarginSchemeGst_ZeroCost_ThrowsDomainValidationException_WithExpectedCode()
    {
        // Cost basis guard: cost <= 0 rejects cost == 0m
        var act = () => TaxEngine.MarginSchemeGst(sell: 10000m, cost: 0m, gstRate: 18m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("margin_scheme_invalid_cost_basis");
        ex.Message.Should().Contain("Margin scheme requires a known positive cost basis (> 0)");
    }

    [Fact]
    public void Case5_MarginSchemeGst_NegativeCost_ThrowsDomainValidationException_WithExpectedCode()
    {
        // Cost basis guard: cost <= 0 rejects negative cost values (cost == -500m)
        var act = () => TaxEngine.MarginSchemeGst(sell: 10000m, cost: -500m, gstRate: 18m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("margin_scheme_invalid_cost_basis");
        ex.Message.Should().Contain("Margin scheme requires a known positive cost basis (> 0)");
    }

    [Fact]
    public void Case6_MarginSchemeGst_ZeroGstRate_ComputesMarginWithZeroTax()
    {
        // Arithmetic:
        // sell = 10000m, cost = 1m, gstRate = 0m
        // margin = sell - cost = 10000 - 1 = 9999m
        // taxable = Round2(9999 / (1 + 0 / 100)) = Round2(9999 / 1) = 9999.00m
        // totalGst = Round2(margin - taxable) = Round2(9999 - 9999.00) = 0.00m
        // cgst = Round2(0 / 2) = 0.00m
        // sgst = Round2(totalGst - cgst) = Round2(0 - 0) = 0.00m
        var result = TaxEngine.MarginSchemeGst(sell: 10000m, cost: 1m, gstRate: 0m);

        result.Taxable.Should().Be(9999.00m);
        result.Cgst.Should().Be(0.00m);
        result.Sgst.Should().Be(0.00m);
        result.TotalGst.Should().Be(0.00m);
    }
}
