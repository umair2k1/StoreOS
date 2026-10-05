using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Domain.Tests.Inventory;

public class BranchInventoryTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public void IncreaseStock_IncrementsStock_AndUpdatesCostPriceWhenPositive()
    {
        var inventory = BranchInventory.Create(TenantId, BranchId, ProductId);

        inventory.IncreaseStock(10, 500m);

        Assert.Equal(10, inventory.StockQty);
        Assert.Equal(500m, inventory.CostPrice);

        inventory.IncreaseStock(5, null);
        Assert.Equal(15, inventory.StockQty);
        Assert.Equal(500m, inventory.CostPrice);
    }

    [Fact]
    public void DecreaseStock_BeyondAvailable_ThrowsInsufficientStock()
    {
        var inventory = BranchInventory.Create(TenantId, BranchId, ProductId);
        inventory.IncreaseStock(5);

        var ex = Assert.Throws<DomainValidationException>(() => inventory.DecreaseStock(10));
        Assert.Equal("insufficient_stock", ex.Code);
    }

    [Fact]
    public void ResolveSellPrice_ReturnsOverrideWhenSet_ElseProductPrice()
    {
        var inventory = BranchInventory.Create(TenantId, BranchId, ProductId);

        Assert.Equal(1000m, inventory.ResolveSellPrice(1000m));

        inventory.SetSellPriceOverride(950m);
        Assert.Equal(950m, inventory.ResolveSellPrice(1000m));

        inventory.SetSellPriceOverride(null);
        Assert.Equal(1000m, inventory.ResolveSellPrice(1000m));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void SetLowStockThreshold_Negative_ThrowsInvalidThreshold(int threshold)
    {
        var inventory = BranchInventory.Create(TenantId, BranchId, ProductId);

        var ex = Assert.Throws<DomainValidationException>(() => inventory.SetLowStockThreshold(threshold));
        Assert.Equal("invalid_threshold", ex.Code);
    }

    [Fact]
    public void SetSellPriceOverride_Negative_ThrowsInvalidSellPrice()
    {
        var inventory = BranchInventory.Create(TenantId, BranchId, ProductId);

        var ex = Assert.Throws<DomainValidationException>(() => inventory.SetSellPriceOverride(-10m));
        Assert.Equal("invalid_sell_price", ex.Code);
    }
}
