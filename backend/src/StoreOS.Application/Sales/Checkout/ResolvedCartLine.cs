namespace StoreOS.Application.Sales.Checkout;

public record ResolvedCartLine(
    Guid ProductId,
    string ProductName,
    string Category,
    int Qty,
    decimal PricePerUnit,
    decimal? PurchasePriceSnapshot,
    string? HsnCode,
    decimal GstRate,
    bool IsMarginScheme,
    bool IsGift,
    string? Imei,
    Guid? ImeiRowId,
    decimal LineTaxable,
    decimal LineCgst,
    decimal LineSgst);
