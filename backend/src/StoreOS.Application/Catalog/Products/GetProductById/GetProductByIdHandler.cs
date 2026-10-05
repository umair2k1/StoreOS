using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Catalog.Products.GetProductById;

public class GetProductByIdHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IAppDbContext _db;

    public GetProductByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<ProductDto> Handle(GetProductByIdQuery request, CancellationToken ct)
    {
        var product = await _db.Set<Product>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, ct);

        if (product == null)
        {
            throw new DomainException("product_not_found", $"Product '{request.Id}' was not found.");
        }

        return new ProductDto(
            product.Id,
            product.Name,
            product.Category,
            product.SellPrice,
            product.PurchasePrice,
            product.GstRate,
            product.IsTaxable,
            product.IsMarginScheme,
            product.HsnCode,
            product.Description,
            product.EstimatedDurationMinutes,
            product.WarrantyDays,
            product.IsActive);
    }
}
