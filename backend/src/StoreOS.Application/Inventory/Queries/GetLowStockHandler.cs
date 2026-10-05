using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Queries;

public class GetLowStockHandler : IRequestHandler<GetLowStockQuery, IReadOnlyList<InventoryLineDto>>
{
    private readonly IReportingDbContext _db;

    public GetLowStockHandler(IReportingDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<InventoryLineDto>> Handle(GetLowStockQuery request, CancellationToken ct)
    {
        var query = from bi in _db.Set<BranchInventory>().AsNoTracking()
                    join p in _db.Set<Product>().AsNoTracking() on bi.ProductId equals p.Id
                    where p.Category != ProductCategory.RepairService
                       && bi.StockQty <= bi.LowStockThreshold
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

        var rawItems = await query
            .OrderBy(x => x.ProductName)
            .ThenBy(x => x.BranchId)
            .ToListAsync(ct);

        var items = rawItems.Select(x =>
        {
            var effectiveSellPrice = x.SellPriceOverride ?? x.ProductSellPrice;
            var status = x.StockQty <= 0 ? "OutOfStock" : "LowStock";

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

        return items;
    }
}
