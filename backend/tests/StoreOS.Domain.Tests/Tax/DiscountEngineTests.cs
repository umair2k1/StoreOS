using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tax;

namespace StoreOS.Domain.Tests.Tax;

public class DiscountEngineTests
{
    [Fact]
    public void Case1_ApplyProportionalDiscount_StandardDiscount_ScalesComponentsProportionally()
    {
        // Arithmetic:
        // before = LineTax(Taxable: 1000m, Cgst: 90m, Sgst: 90m)
        // subtotal = 1180m, discount = 118m
        // grand = subtotal - discount = 1180 - 118 = 1062m
        // factor = grand / subtotal = 1062 / 1180 = 0.9m
        // taxable = Round2(1000 * 0.9) = 900.00m
        // cgst = Round2(90 * 0.9) = 81.00m
        // sgst = Round2(90 * 0.9) = 81.00m
        var before = new LineTax(1000m, 90m, 90m);
        var result = DiscountEngine.ApplyProportionalDiscount(before, subtotal: 1180m, discount: 118m);

        result.Taxable.Should().Be(900.00m);
        result.Cgst.Should().Be(81.00m);
        result.Sgst.Should().Be(81.00m);
        result.TotalGst.Should().Be(162.00m);
    }

    [Fact]
    public void Case2_ApplyProportionalDiscount_ZeroDiscount_ReturnsIdenticalResult()
    {
        // factor = (1180 - 0) / 1180 = 1.0m
        var before = new LineTax(1000m, 90m, 90m);
        var result = DiscountEngine.ApplyProportionalDiscount(before, subtotal: 1180m, discount: 0m);

        result.Should().Be(before);
        result.Taxable.Should().Be(before.Taxable);
        result.Cgst.Should().Be(before.Cgst);
        result.Sgst.Should().Be(before.Sgst);
        result.TotalGst.Should().Be(before.TotalGst);
    }

    [Fact]
    public void Case3_ApplyProportionalDiscount_FullDiscount_ReturnsZeroComponents()
    {
        // discount == subtotal (100% discount) -> grand = 0 -> factor = 0 -> all fields 0.00m
        var before = new LineTax(1000m, 90m, 90m);
        var result = DiscountEngine.ApplyProportionalDiscount(before, subtotal: 1180m, discount: 1180m);

        result.Taxable.Should().Be(0m);
        result.Cgst.Should().Be(0m);
        result.Sgst.Should().Be(0m);
        result.TotalGst.Should().Be(0m);
        result.Should().Be(LineTax.Zero);
    }

    [Fact]
    public void Case4_ApplyProportionalDiscount_DiscountExceedsSubtotal_ThrowsDomainValidationException()
    {
        var before = new LineTax(1000m, 90m, 90m);
        var act = () => DiscountEngine.ApplyProportionalDiscount(before, subtotal: 1000m, discount: 1001m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("discount_exceeds_subtotal");
        ex.Message.Should().Contain("Discount cannot exceed subtotal.");
    }

    [Fact]
    public void Case5_ApplyProportionalDiscount_NegativeDiscount_ThrowsDomainValidationException()
    {
        var before = new LineTax(1000m, 90m, 90m);
        var act = () => DiscountEngine.ApplyProportionalDiscount(before, subtotal: 1000m, discount: -10m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("discount_negative");
        ex.Message.Should().Contain("Discount cannot be negative.");
    }

    [Fact]
    public void Case6_ApplyProportionalDiscount_ZeroSubtotalAndZeroDiscount_ReturnsBeforeUnchanged()
    {
        var before = new LineTax(500m, 45m, 45m);
        var result = DiscountEngine.ApplyProportionalDiscount(before, subtotal: 0m, discount: 0m);

        result.Should().Be(before);
    }

    [Fact]
    public void Case7_ApplyProportionalDiscount_MultiLineScenario_ScalesBothLinesIdentically()
    {
        // Subtotal here is the pre-discount GROSS invoice subtotal (sum of unitPrice * qty across lines),
        // NOT the sum of taxable bases.
        // Line 1: gross = 590m, before = LineTax(500m, 45m, 45m)
        // Line 2: gross = 590m, before = LineTax(500m, 45m, 45m)
        // Gross subtotal = 590 + 590 = 1180m
        // Invoice discount = 118m (10% overall discount)
        // Factor = (1180 - 118) / 1180 = 1062 / 1180 = 0.9m
        var line1Before = new LineTax(500m, 45m, 45m);
        var line2Before = new LineTax(500m, 45m, 45m);
        const decimal grossSubtotal = 1180m;
        const decimal invoiceDiscount = 118m;

        var line1After = DiscountEngine.ApplyProportionalDiscount(line1Before, grossSubtotal, invoiceDiscount);
        var line2After = DiscountEngine.ApplyProportionalDiscount(line2Before, grossSubtotal, invoiceDiscount);

        // Expected per line:
        // taxable = Round2(500 * 0.9) = 450.00m
        // cgst = Round2(45 * 0.9) = 40.50m
        // sgst = Round2(45 * 0.9) = 40.50m
        line1After.Taxable.Should().Be(450.00m);
        line1After.Cgst.Should().Be(40.50m);
        line1After.Sgst.Should().Be(40.50m);
        line1After.TotalGst.Should().Be(81.00m);

        line2After.Taxable.Should().Be(450.00m);
        line2After.Cgst.Should().Be(40.50m);
        line2After.Sgst.Should().Be(40.50m);
        line2After.TotalGst.Should().Be(81.00m);

        line1After.Should().Be(line2After);
    }

    [Fact]
    public void Case8_ApplyProportionalDiscount_ZeroSubtotalWithNegativeDiscount_ThrowsDiscountNegative()
    {
        // Proves validation ordering: discount < 0 runs before the subtotal == 0 return early guard.
        var act = () => DiscountEngine.ApplyProportionalDiscount(LineTax.Zero, subtotal: 0m, discount: -5m);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("discount_negative");
    }

    [Fact]
    public void Case9_ApplyProportionalDiscount_NegativeSubtotal_ThrowsArgumentOutOfRangeException()
    {
        var act = () => DiscountEngine.ApplyProportionalDiscount(LineTax.Zero, subtotal: -10m, discount: 0m);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }
}
