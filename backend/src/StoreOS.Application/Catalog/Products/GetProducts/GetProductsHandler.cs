using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;

namespace StoreOS.Application.Catalog.Products.GetProducts;

public class GetProductsHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IAppDbContext _db;

    public GetProductsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken ct)
    {
        var query = _db.Set<Product>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(p => p.Category == request.Category);
        }

        if (request.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == request.IsActive.Value);
        }

        if (request.IsMarginScheme.HasValue)
        {
            query = query.Where(p => p.IsMarginScheme == request.IsMarginScheme.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), search.ToLower()));
        }

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : request.PageSize;

        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Category,
                p.SellPrice,
                p.PurchasePrice,
                p.GstRate,
                p.IsTaxable,
                p.IsMarginScheme,
                p.HsnCode,
                p.Description,
                p.EstimatedDurationMinutes,
                p.WarrantyDays,
                p.IsActive))
            .ToListAsync(ct);

        return new PagedResult<ProductDto>(items, totalCount, page, pageSize);
    }
}
