using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Queries;

public class GetInventoryCategoriesHandler : IRequestHandler<GetInventoryCategoriesQuery, IReadOnlyList<string>>
{
    private readonly IReportingDbContext _db;

    public GetInventoryCategoriesHandler(IReportingDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<string>> Handle(GetInventoryCategoriesQuery request, CancellationToken ct)
    {
        var categories = await (from bi in _db.Set<BranchInventory>().AsNoTracking()
                                join p in _db.Set<Product>().AsNoTracking() on bi.ProductId equals p.Id
                                select p.Category)
                               .Distinct()
                               .OrderBy(c => c)
                               .ToListAsync(ct);

        if (categories.Count == 0)
        {
            categories = await _db.Set<Product>().AsNoTracking()
                                  .Select(p => p.Category)
                                  .Distinct()
                                  .OrderBy(c => c)
                                  .ToListAsync(ct);
        }

        return categories;
    }
}
