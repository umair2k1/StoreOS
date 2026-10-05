using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Sales.Search;

public class GetPosProductHandler : IRequestHandler<GetPosProductQuery, PosProductDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly IBranchContext _branchContext;

    public GetPosProductHandler(IAppDbContext db, IBranchContext branchContext)
    {
        _db = db;
        _branchContext = branchContext;
    }

    public async Task<PosProductDetailDto> Handle(GetPosProductQuery request, CancellationToken ct)
    {
        var product = await _db.Set<Product>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.IsActive, ct);

        if (product == null)
        {
            throw new DomainException("product_not_found", $"Product '{request.Id}' was not found.");
        }

        var currentBranchId = _branchContext.CurrentBranchId;

        int stockQty;
        string stockStatus;

        if (product.Category == ProductCategory.RepairService)
        {
            stockQty = 0;
            stockStatus = "N/A";
        }
        else
        {
            var invQuery = _db.Set<BranchInventory>().AsNoTracking()
                .Where(bi => bi.ProductId == product.Id);

            if (currentBranchId.HasValue)
            {
                invQuery = invQuery.Where(bi => bi.BranchId == currentBranchId.Value);
            }

            var inventoryRows = await invQuery.ToListAsync(ct);
            stockQty = inventoryRows.Sum(x => x.StockQty);
            var threshold = inventoryRows.Count > 0 ? inventoryRows.Max(x => x.LowStockThreshold) : 3;

            if (stockQty <= 0)
            {
                stockStatus = "OutOfStock";
            }
            else if (stockQty <= threshold)
            {
                stockStatus = "LowStock";
            }
            else
            {
                stockStatus = "InStock";
            }
        }

        // Available IMEIs currently InStock at current branch
        List<string> availableImeis = new();
        if (product.Category != ProductCategory.RepairService)
        {
            var imeiQuery = _db.Set<InventoryImei>().AsNoTracking()
                .Where(i => i.ProductId == product.Id && i.Status == ImeiStatus.InStock);

            if (currentBranchId.HasValue)
            {
                imeiQuery = imeiQuery.Where(i => i.BranchId == currentBranchId.Value);
            }

            availableImeis = await imeiQuery
                .OrderBy(i => i.Imei)
                .Select(i => i.Imei)
                .ToListAsync(ct);
        }

        return new PosProductDetailDto(
            product.Id,
            product.Name,
            product.Category,
            product.SellPrice,
            product.GstRate,
            product.HsnCode,
            product.IsMarginScheme,
            stockQty,
            stockStatus,
            availableImeis.AsReadOnly());
    }
}
