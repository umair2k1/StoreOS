using FluentAssertions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Domain.Tests.Inventory;

public class ValuationCalculatorTests
{
    [Fact]
    public void Case1_Calculate_MixedCategories_ExcludesRepairServiceCategory()
    {
        // Arithmetic:
        // Line 1: NewPhone, qty 5, cost 10000m, sell 12000m -> cost = 50000m, retail = 60000m
        // Line 2: Accessory, qty 20, cost 100m, sell 150m -> cost = 2000m, retail = 3000m
        // Line 3: RepairService, qty 999, cost 500m, sell 800m -> excluded entirely
        // Total cost = 50000 + 2000 = 52000.00m
        // Total retail = 60000 + 3000 = 63000.00m
        // Potential gross = retail - cost = 63000 - 52000 = 11000.00m
        var lines = new[]
        {
            new StockValuationLine("NewPhone", 5, 10000m, 12000m),
            new StockValuationLine("Accessory", 20, 100m, 150m),
            new StockValuationLine("RepairService", 999, 500m, 800m)
        };

        var result = ValuationCalculator.Calculate(lines);

        result.CostValuation.Should().Be(52000.00m);
        result.RetailValuation.Should().Be(63000.00m);
        result.PotentialGrossValue.Should().Be(11000.00m);
    }

    [Fact]
    public void Case2_Calculate_EmptyLines_ReturnsAllZeros()
    {
        var lines = Array.Empty<StockValuationLine>();

        var result = ValuationCalculator.Calculate(lines);

        result.CostValuation.Should().Be(0m);
        result.RetailValuation.Should().Be(0m);
        result.PotentialGrossValue.Should().Be(0m);
    }

    [Fact]
    public void Case3_Calculate_ZeroQuantityLine_ContributesZeroWithoutCrashing()
    {
        var lines = new[]
        {
            new StockValuationLine("NewPhone", 0, 15000m, 18000m)
        };

        var result = ValuationCalculator.Calculate(lines);

        result.CostValuation.Should().Be(0m);
        result.RetailValuation.Should().Be(0m);
        result.PotentialGrossValue.Should().Be(0m);
    }

    [Fact]
    public void Case4_IsLowStock_QuantityAtThreshold_ReturnsTrue()
    {
        ValuationCalculator.IsLowStock(qty: 3, threshold: 3).Should().BeTrue();
    }

    [Fact]
    public void Case5_IsLowStock_QuantityAboveThreshold_ReturnsFalse()
    {
        ValuationCalculator.IsLowStock(qty: 4, threshold: 3).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void Case6_IsOutOfStock_EvaluatesBoundaryConditions(int qty, bool expected)
    {
        ValuationCalculator.IsOutOfStock(qty).Should().Be(expected);
    }
}
