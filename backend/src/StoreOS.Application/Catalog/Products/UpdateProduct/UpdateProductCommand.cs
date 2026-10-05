using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Catalog.Products.UpdateProduct;

public record UpdateProductCommand(
    Guid Id,
    string? Name = null,
    decimal? SellPrice = null,
    decimal? PurchasePrice = null,
    decimal? GstRate = null,
    bool? IsMarginScheme = null,
    string? Description = null,
    int? EstimatedDurationMinutes = null,
    int? WarrantyDays = null,
    bool? IsActive = null) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "inventory:manage";
}
