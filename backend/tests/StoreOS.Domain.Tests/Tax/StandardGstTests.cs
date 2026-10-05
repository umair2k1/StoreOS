using FluentAssertions;
using StoreOS.Domain.Tax;

namespace StoreOS.Domain.Tests.Tax;

public class StandardGstTests
{
    [Fact]
    public void Case1_StandardGst_1180_At_18Percent_Returns_1000_Taxable_And_90_Halves()
    {
        // Arithmetic:
        // taxable = Round2(1180 / 1.18) = Round2(1000.0) = 1000.00m
        // totalGst = Round2(1180 - 1000) = 180.00m
        // cgst = Round2(180 / 2) = 90.00m
        // sgst = 180 - 90 = 90.00m
        var result = TaxEngine.StandardGst(1180m, 18m);

        result.Taxable.Should().Be(1000.00m);
        result.Cgst.Should().Be(90.00m);
        result.Sgst.Should().Be(90.00m);
        result.TotalGst.Should().Be(180.00m);
    }

    [Fact]
    public void Case2_StandardGst_1000_At_0Percent_Returns_1000_Taxable_And_0_Gst()
    {
        // Arithmetic:
        // taxable = Round2(1000 / 1.0) = 1000.00m
        // totalGst = Round2(1000 - 1000) = 0.00m
        // cgst = Round2(0 / 2) = 0.00m
        // sgst = 0 - 0 = 0.00m
        var result = TaxEngine.StandardGst(1000m, 0m);

        result.Taxable.Should().Be(1000.00m);
        result.Cgst.Should().Be(0.00m);
        result.Sgst.Should().Be(0.00m);
        result.TotalGst.Should().Be(0.00m);
    }

    [Fact]
    public void Case3_StandardGst_1250_At_12Percent_Reconciles_Odd_Paise_Correctly()
    {
        // Arithmetic:
        // taxable = Round2(1250 / 1.12) = Round2(1116.07142857...) = 1116.07m
        // totalGst = Round2(1250 - 1116.07) = 133.93m
        // cgst = Round2(133.93 / 2) = Round2(66.965m) = 66.97m (AwayFromZero rounding)
        // sgst = 133.93 - 66.97 = 66.96m
        var result = TaxEngine.StandardGst(1250m, 12m);

        result.Taxable.Should().Be(1116.07m);
        result.Cgst.Should().Be(66.97m);
        result.Sgst.Should().Be(66.96m);
        result.TotalGst.Should().Be(133.93m);
        (result.Cgst + result.Sgst).Should().Be(result.TotalGst);
        (result.Taxable + result.TotalGst).Should().Be(1250m);
    }

    [Fact]
    public void Case4_StandardGst_0_At_18Percent_Returns_All_Zero()
    {
        // Arithmetic:
        // taxable = Round2(0 / 1.18) = 0.00m
        // totalGst = Round2(0 - 0) = 0.00m
        // cgst = Round2(0 / 2) = 0.00m
        // sgst = 0 - 0 = 0.00m
        var result = TaxEngine.StandardGst(0m, 18m);

        result.Taxable.Should().Be(0m);
        result.Cgst.Should().Be(0m);
        result.Sgst.Should().Be(0m);
        result.TotalGst.Should().Be(0m);
    }

    [Fact]
    public void Case5_StandardGst_999_99_At_18Percent_Returns_Expected_Tax_Breakdown()
    {
        // Arithmetic:
        // taxable = Round2(999.99 / 1.18) = Round2(847.44915254...) = 847.45m
        // totalGst = Round2(999.99 - 847.45) = 152.54m
        // cgst = Round2(152.54 / 2) = Round2(76.27m) = 76.27m
        // sgst = 152.54 - 76.27 = 76.27m
        var result = TaxEngine.StandardGst(999.99m, 18m);

        result.Taxable.Should().Be(847.45m);
        result.Cgst.Should().Be(76.27m);
        result.Sgst.Should().Be(76.27m);
        result.TotalGst.Should().Be(152.54m);
        (result.Cgst + result.Sgst).Should().Be(result.TotalGst);
        (result.Taxable + result.TotalGst).Should().Be(999.99m);
    }

    [Fact]
    public void Case6_MultiLine_Reconciliation_Reconciles_Sum_Of_Taxable_And_Gst_With_Sum_Of_LineTotals()
    {
        // Lines:
        // Line 1: 1180m @ 18% -> taxable=1000.00, cgst=90.00, sgst=90.00 (sum=1180.00)
        // Line 2: 1250m @ 12% -> taxable=1116.07, cgst=66.97, sgst=66.96 (sum=1250.00)
        // Line 3: 500m @ 0%   -> taxable=500.00,  cgst=0.00,  sgst=0.00  (sum=500.00)
        var line1 = TaxEngine.StandardGst(1180m, 18m);
        var line2 = TaxEngine.StandardGst(1250m, 12m);
        var line3 = TaxEngine.StandardGst(500m, 0m);

        var sumTaxable = line1.Taxable + line2.Taxable + line3.Taxable;
        var sumCgst = line1.Cgst + line2.Cgst + line3.Cgst;
        var sumSgst = line1.Sgst + line2.Sgst + line3.Sgst;
        var sumLineTotals = 1180m + 1250m + 500m;

        (sumTaxable + sumCgst + sumSgst).Should().Be(sumLineTotals);
    }

    [Theory]
    [InlineData(-1, 18)]
    [InlineData(-100.50, 12)]
    [InlineData(100, -1)]
    [InlineData(100, -18)]
    [InlineData(-50, -5)]
    public void Case7_Negative_LineTotal_Or_GstRate_Throws_ArgumentOutOfRangeException(double lineTotal, double gstRate)
    {
        var act = () => TaxEngine.StandardGst((decimal)lineTotal, (decimal)gstRate);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
