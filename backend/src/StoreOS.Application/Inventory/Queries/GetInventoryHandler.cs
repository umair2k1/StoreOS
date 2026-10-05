using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Queries;

public class GetInventoryHandler : IRequestHandler<GetInventoryQuery, PagedResult<InventoryLineDto>>
{
    private readonly IReportingDbContext _db;

    public GetInventoryHandler(IReportingDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<InventoryLineDto>> Handle(GetInventoryQuery request, CancellationToken ct)
    {
        var query = from bi in _db.Set<BranchInventory>().AsNoTracking()
                    join p in _db.Set<Product>().AsNoTracking() on bi.ProductId equals p.Id
                    select new
                    {
                        bi.ProductId,
                        bi.BranchId,
                        ProductName = p.Name,
                        p.Category,
                        bi.StockQty,
                        bi.CostPrice,
                        bi.SellPriceOverride,
                        ProductSellPrice = p.SellPrice,
                        p.GstRate,
                        p.IsMarginScheme,
                        p.HsnCode,
                        bi.LowStockThreshold
                    };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            query = query.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(x => x.Category == request.Category);
        }

        if (request.IsMarginScheme.HasValue)
        {
            query = query.Where(x => x.IsMarginScheme == request.IsMarginScheme.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim()}%";
            query = query.Where(x => EF.Functions.Like(x.ProductName.ToLower(), search.ToLower()));
        }

        if (!string.IsNullOrWhiteSpace(request.StockStatus))
        {
            if (string.Equals(request.StockStatus, "OutOfStock", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.StockQty <= 0);
            }
            else if (string.Equals(request.StockStatus, "LowStock", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.StockQty > 0 && x.StockQty <= x.LowStockThreshold);
            }
            else if (string.Equals(request.StockStatus, "InStock", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => x.StockQty > x.LowStockThreshold);
            }
        }

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : request.PageSize;

        var rawItems = await query
            .OrderBy(x => x.ProductName)
            .ThenBy(x => x.BranchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rawItems.Select(x =>
        {
            var effectiveSellPrice = x.SellPriceOverride ?? x.ProductSellPrice;
            string status;
            if (x.StockQty <= 0)
            {
                status = "OutOfStock";
            }
            else if (x.StockQty <= x.LowStockThreshold)
            {
                status = "LowStock";
            }
            else
            {
                status = "InStock";
            }

            return new InventoryLineDto(
                x.ProductId,
                x.BranchId,
                x.ProductName,
                x.Category,
                x.StockQty,
                x.CostPrice,
                effectiveSellPrice,
                x.GstRate,
                x.IsMarginScheme,
                x.HsnCode,
                status);
        }).ToList();

        return new PagedResult<InventoryLineDto>(items, totalCount, page, pageSize);
    }
}
