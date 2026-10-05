namespace StoreOS.Application.Catalog.Products;

public record ProductDto(
    Guid Id,
    string Name,
    string Category,
    decimal SellPrice,
    decimal PurchasePrice,
    decimal GstRate,
    bool IsTaxable,
    bool IsMarginScheme,
    string? HsnCode,
    string? Description,
    int? EstimatedDurationMinutes,
    int? WarrantyDays,
    bool IsActive);
