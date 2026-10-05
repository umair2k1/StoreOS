using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Purchasing.StockIn;

public record StockInCommand(Guid ProductId, int Qty, decimal PurchaseRate,
    Guid? SupplierId, string? SupplierName, string? SupplierPhone, string? SupplierAddress,
    DateOnly PurchaseDate, string? SupplierInvoiceNo, IReadOnlyList<string>? Imeis)
    : IRequest<StockInResult>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}

public record StockInResult(Guid PurchaseId, int NewStockQty, IReadOnlyList<Guid> CreatedImeiIds);
