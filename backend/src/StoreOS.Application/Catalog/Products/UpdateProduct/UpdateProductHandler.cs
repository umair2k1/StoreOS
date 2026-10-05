using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Catalog.Products.UpdateProduct;

public class UpdateProductHandler : IRequestHandler<UpdateProductCommand>
{
    private readonly IAppDbContext _db;

    public UpdateProductHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateProductCommand request, CancellationToken ct)
    {
        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (product == null)
        {
            throw new DomainException("product_not_found", $"Product '{request.Id}' was not found.");
        }

        if (request.Name != null)
        {
            product.Rename(request.Name);
        }

        if (request.SellPrice.HasValue || request.PurchasePrice.HasValue || request.GstRate.HasValue)
        {
            product.UpdatePricing(
                request.SellPrice ?? product.SellPrice,
                request.PurchasePrice ?? product.PurchasePrice,
                request.GstRate ?? product.GstRate);
        }

        if (request.IsMarginScheme.HasValue)
        {
            product.SetMarginScheme(request.IsMarginScheme.Value);
        }

        if (request.Description != null || request.EstimatedDurationMinutes.HasValue || request.WarrantyDays.HasValue)
        {
            product.UpdateDescriptionAndServiceFields(
                request.Description ?? product.Description,
                request.EstimatedDurationMinutes ?? product.EstimatedDurationMinutes,
                request.WarrantyDays ?? product.WarrantyDays);
        }

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                product.Activate();
            }
            else
            {
                product.Deactivate();
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
