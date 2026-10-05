namespace StoreOS.Domain.Inventory;

public readonly record struct StockValuationLine(string Category, int Qty, decimal PurchasePrice, decimal SellPrice);
