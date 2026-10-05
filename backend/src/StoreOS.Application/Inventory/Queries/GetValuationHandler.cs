using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Queries;

public class GetValuationHandler : IRequestHandler<GetValuationQuery, ValuationDto>
{
    private readonly IReportingDbContext _db;

    public GetValuationHandler(IReportingDbContext db)
    {
        _db = db;
    }

    public async Task<ValuationDto> Handle(GetValuationQuery request, CancellationToken ct)
    {
        var query = from bi in _db.Set<BranchInventory>().AsNoTracking()
                    join p in _db.Set<Product>().AsNoTracking() on bi.ProductId equals p.Id
                    select new
                    {
                        bi.BranchId,
                        p.Category,
                        bi.StockQty,
                        bi.CostPrice,
                        bi.SellPriceOverride,
                        ProductSellPrice = p.SellPrice
                    };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            query = query.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        var rows = await query.ToListAsync(ct);

        var valuationLines = rows.Select(x =>
        {
            var sellPrice = x.SellPriceOverride ?? x.ProductSellPrice;
            return new StockValuationLine(x.Category, x.StockQty, x.CostPrice, sellPrice);
        });

        var result = ValuationCalculator.Calculate(valuationLines);

        return new ValuationDto(result.CostValuation, result.RetailValuation, result.PotentialGrossValue);
    }
}
