using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;

namespace StoreOS.Application.Repairs.Services.GetRepairServices;

public class GetRepairServicesHandler : IRequestHandler<GetRepairServicesQuery, PagedResult<RepairServiceDto>>
{
    private readonly IAppDbContext _db;

    public GetRepairServicesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<RepairServiceDto>> Handle(GetRepairServicesQuery request, CancellationToken ct)
    {
        var query = _db.Set<Product>()
            .AsNoTracking()
            .Where(p => p.Category == ProductCategory.RepairService);

        if (request.IsActive == true)
        {
            query = query.Where(p => p.IsActive);
        }

        var totalCount = await query.CountAsync(ct);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new RepairServiceDto(
                p.Id,
                p.Name,
                p.Description,
                p.SellPrice,
                p.IsTaxable,
                p.GstRate,
                p.HsnCode,
                p.EstimatedDurationMinutes,
                p.WarrantyDays,
                p.IsActive))
            .ToListAsync(ct);

        return new PagedResult<RepairServiceDto>(items, totalCount, page, pageSize);
    }
}
