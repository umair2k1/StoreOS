using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;

namespace StoreOS.Application.Catalog.Products.CreateProduct;

public class CreateProductHandler : IRequestHandler<CreateProductCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public CreateProductHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken ct)
    {
        var product = Product.Create(
            _scope.TenantId,
            request.Name,
            request.Category,
            request.SellPrice,
            request.PurchasePrice,
            request.GstRate,
            request.HsnCode);

        if (request.Description != null || request.EstimatedDurationMinutes != null || request.WarrantyDays != null)
        {
            product.UpdateDescriptionAndServiceFields(request.Description, request.EstimatedDurationMinutes, request.WarrantyDays);
        }

        if (request.IsMarginScheme)
        {
            product.SetMarginScheme(true);
        }

        await _db.Set<Product>().AddAsync(product, ct);
        await _db.SaveChangesAsync(ct);
        return product.Id;
    }
}
