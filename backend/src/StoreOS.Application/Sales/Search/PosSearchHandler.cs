using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Sales.Search;

public class PosSearchHandler : IRequestHandler<PosSearchQuery, IReadOnlyList<PosSearchResultDto>>
{
    private readonly IAppDbContext _db;
    private readonly IBranchContext _branchContext;

    public PosSearchHandler(IAppDbContext db, IBranchContext branchContext)
    {
        _db = db;
        _branchContext = branchContext;
    }

    public async Task<IReadOnlyList<PosSearchResultDto>> Handle(PosSearchQuery request, CancellationToken ct)
    {
        var rawQuery = (request.Q ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return Array.Empty<PosSearchResultDto>();
        }

        var currentBranchId = _branchContext.CurrentBranchId;

        // 1. Check if rawQuery looks like an IMEI (14-16 digits)
        var isImeiCandidate = rawQuery.Length >= 14 && rawQuery.Length <= 16 && rawQuery.All(char.IsAsciiDigit);
        List<Guid> imeiProductIds = new();

        if (isImeiCandidate)
        {
            var imeiQuery = _db.Set<InventoryImei>().AsNoTracking()
                .Where(x => x.Imei == rawQuery && x.Status == ImeiStatus.InStock);

            if (currentBranchId.HasValue)
            {
                imeiQuery = imeiQuery.Where(x => x.BranchId == currentBranchId.Value);
            }

            imeiProductIds = await imeiQuery
                .Select(x => x.ProductId)
                .Distinct()
                .ToListAsync(ct);
        }

        // 2. Name search matching products case-insensitively
        var searchPattern = $"%{rawQuery.ToLowerInvariant()}%";
        var productsQuery = _db.Set<Product>().AsNoTracking()
            .Where(p => p.IsActive && (EF.Functions.Like(p.Name.ToLower(), searchPattern) || imeiProductIds.Contains(p.Id)));
        var matchedProducts = await productsQuery.ToListAsync(ct);
        if (matchedProducts.Count == 0)
        {
            return Array.Empty<PosSearchResultDto>();
        }

        var matchedProductIds = matchedProducts.Select(p => p.Id).ToList();

        // 3. Load branch inventory for these products
        var inventoryQuery = _db.Set<BranchInventory>().AsNoTracking()
            .Where(bi => matchedProductIds.Contains(bi.ProductId));

        if (currentBranchId.HasValue)
        {
            inventoryQuery = inventoryQuery.Where(bi => bi.BranchId == currentBranchId.Value);
        }

        var inventoryRows = await inventoryQuery.ToListAsync(ct);

        // Group inventory by ProductId
        var inventoryByProduct = inventoryRows
            .GroupBy(bi => bi.ProductId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    StockQty = g.Sum(x => x.StockQty),
                    LowStockThreshold = g.Max(x => x.LowStockThreshold)
                });

        var results = new List<PosSearchResultDto>();

        foreach (var product in matchedProducts)
        {
            int stockQty;
            string stockStatus;

            if (product.Category == ProductCategory.RepairService)
            {
                stockQty = 0;
                stockStatus = "N/A";
            }
            else
            {
                if (inventoryByProduct.TryGetValue(product.Id, out var inv))
                {
                    stockQty = inv.StockQty;
                    if (stockQty <= 0)
                    {
                        stockStatus = "OutOfStock";
                    }
                    else if (stockQty <= inv.LowStockThreshold)
                    {
                        stockStatus = "LowStock";
                    }
                    else
                    {
                        stockStatus = "InStock";
                    }
                }
                else
                {
                    stockQty = 0;
                    stockStatus = "OutOfStock";
                }
            }

            results.Add(new PosSearchResultDto(
                product.Id,
                product.Name,
                product.Category,
                product.SellPrice,
                product.GstRate,
                product.HsnCode,
                product.IsMarginScheme,
                stockQty,
                stockStatus));
        }

        // Order results so exact IMEI matches appear first, followed by name ordering
        if (imeiProductIds.Count > 0)
        {
            results = results
                .OrderByDescending(r => imeiProductIds.Contains(r.ProductId))
                .ThenBy(r => r.Name)
                .ToList();
        }
        else
        {
            results = results.OrderBy(r => r.Name).ToList();
        }

        return results;
    }
}
