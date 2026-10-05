using StoreOS.Domain.Common;

namespace StoreOS.Domain.Inventory;

public static class ValuationCalculator
{
    private const string RepairServiceCategory = "RepairService";

    public static ValuationResult Calculate(IEnumerable<StockValuationLine> lines)
    {
        decimal cost = 0m, retail = 0m;
        foreach (var line in lines)
        {
            if (line.Category == RepairServiceCategory) continue; // Invariant 3: services never count as stock
            cost += line.Qty * line.PurchasePrice;
            retail += line.Qty * line.SellPrice;
        }
        cost = Money.Round2(cost);
        retail = Money.Round2(retail);
        return new ValuationResult(cost, retail, Money.Round2(retail - cost));
    }

    public static bool IsLowStock(int qty, int threshold) => qty <= threshold;
    public static bool IsOutOfStock(int qty) => qty <= 0;
}
