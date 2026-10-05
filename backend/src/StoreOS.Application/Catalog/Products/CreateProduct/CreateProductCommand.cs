using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Catalog.Products.CreateProduct;

public record CreateProductCommand(
    string Name,
    string Category,
    decimal SellPrice,
    decimal PurchasePrice,
    decimal GstRate,
    string? HsnCode,
    string? Description = null,
    int? EstimatedDurationMinutes = null,
    int? WarrantyDays = null,
    bool IsMarginScheme = false) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "inventory:manage";
}
