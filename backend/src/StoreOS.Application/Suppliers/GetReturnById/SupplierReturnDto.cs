namespace StoreOS.Application.Suppliers.GetReturnById;

public record SupplierReturnItemDto(
    Guid Id,
    string ItemName,
    int Qty,
    decimal ReturnRate,
    decimal LineTotal,
    string[]? ImeiNumbers
);

public record SupplierReturnDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string ReturnNumber,
    DateOnly ReturnDate,
    Guid SupplierId,
    string SupplierName,
    string? SupplierPhone,
    string? SupplierAddress,
    int TotalQty,
    decimal TotalAmount,
    string SettlementType,
    string? Reason,
    string? Notes,
    IReadOnlyList<SupplierReturnItemDto> Items
);
