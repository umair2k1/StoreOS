namespace StoreOS.Application.Purchasing.GetPurchases;

public record PurchaseDto(
    Guid Id,
    Guid BranchId,
    Guid ProductId,
    int Qty,
    decimal PurchaseRate,
    Guid? SupplierId,
    string? SupplierName,
    string? SupplierPhone,
    string? SupplierAddress,
    string? SellerName,
    string? SellerPhone,
    string? SellerAddress,
    string? SupplierInvoiceNo,
    DateOnly PurchaseDate,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);
