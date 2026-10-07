using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public class BranchInventory
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public int StockQty { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal? SellPriceOverride { get; private set; }
    public int LowStockThreshold { get; private set; } = 3;

    private BranchInventory() { }

    public static BranchInventory Create(Guid tenantId, Guid branchId, Guid productId)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchId, ProductId = productId };

    public static BranchInventory Rehydrate(
        Guid id, Guid tenantId, Guid branchId, Guid productId,
        int stockQty, decimal costPrice, decimal? sellPriceOverride, int lowStockThreshold)
    {
        return new BranchInventory
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            StockQty = stockQty,
            CostPrice = costPrice,
            SellPriceOverride = sellPriceOverride,
            LowStockThreshold = lowStockThreshold
        };
    }

    public void IncreaseStock(int qty, decimal? newCostPrice = null)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");
        StockQty += qty;
        if (newCostPrice is not null && newCostPrice.Value > 0) CostPrice = newCostPrice.Value;
    }

    public void DecreaseStock(int qty)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");
        if (StockQty < qty) throw new DomainValidationException("insufficient_stock", "Not enough stock available.");
        StockQty -= qty;
    }

    public void SetSellPriceOverride(decimal? price)
    {
        if (price is not null && price < 0) throw new DomainValidationException("invalid_sell_price", "Sell price cannot be negative.");
        SellPriceOverride = price;
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0) throw new DomainValidationException("invalid_threshold", "Threshold cannot be negative.");
        LowStockThreshold = threshold;
    }

    public decimal ResolveSellPrice(decimal productSellPrice) => SellPriceOverride ?? productSellPrice;
}
