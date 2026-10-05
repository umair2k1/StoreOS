using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.GetSuppliers;

public class GetSuppliersHandler : IRequestHandler<GetSuppliersQuery, PagedResult<SupplierDto>>
{
    private readonly IAppDbContext _db;

    public GetSuppliersHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<SupplierDto>> Handle(GetSuppliersQuery request, CancellationToken ct)
    {
        var query = _db.Set<Supplier>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim()}%";
            query = query.Where(s => EF.Functions.Like(s.Name.ToLower(), search.ToLower()));
        }

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : request.PageSize;

        var items = await query
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SupplierDto(
                s.Id,
                s.Name,
                s.Phone,
                s.Address,
                s.BrandOrCategory,
                s.Notes,
                s.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<SupplierDto>(items, totalCount, page, pageSize);
    }
}
