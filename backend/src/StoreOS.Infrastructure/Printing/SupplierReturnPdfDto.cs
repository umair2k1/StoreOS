namespace StoreOS.Infrastructure.Printing;

public record SupplierReturnLineDto(
    string ItemName,
    int Qty,
    decimal ReturnRate,
    decimal LineTotal,
    string[]? ImeiNumbers
);

public record SupplierReturnPdfDto(
    string ShopName,
    string ReturnNumber,
    DateOnly ReturnDate,
    string SupplierName,
    string? SupplierPhone,
    string? SupplierAddress,
    IReadOnlyList<SupplierReturnLineDto> Lines,
    decimal TotalAmount,
    string SettlementType,
    string? Reason,
    byte[]? LogoBytes = null,
    string? ShopAddress = null,
    string? ShopPhone = null
);
