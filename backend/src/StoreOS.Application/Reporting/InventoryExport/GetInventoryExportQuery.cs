using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;

namespace StoreOS.Application.Reporting.InventoryExport;

public record GetInventoryExportQuery(string[] Categories, Guid[]? BranchIds = null)
    : IRequest<InventoryExportDto>, IReportingRequest;

public record PhysicalInventoryExportRowDto(
    string Item,
    string Category,
    int Quantity,
    decimal PurchasePrice,
    decimal SellingPrice,
    decimal Gst,
    string? Hsn,
    string? Supplier,
    string? ImeiSummary);

public record ServiceExportRowDto(
    string Service,
    string? Description,
    decimal ServiceCharge,
    bool Taxable,
    decimal Gst,
    string? Sac);

public record InventoryExportDto(
    IReadOnlyList<PhysicalInventoryExportRowDto> PhysicalItems,
    IReadOnlyList<ServiceExportRowDto> Services);

public class GetInventoryExportHandler : IRequestHandler<GetInventoryExportQuery, InventoryExportDto>
{
    private readonly IReportingDbContext _db;
    private readonly IAccessScope _scope;

    public GetInventoryExportHandler(IReportingDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<InventoryExportDto> Handle(GetInventoryExportQuery request, CancellationToken ct)
    {
        var requestedCategories = (request.Categories == null || request.Categories.Length == 0)
            ? ProductCategory.All
            : request.Categories;

        bool wantsRepairService = requestedCategories.Contains(ProductCategory.RepairService, StringComparer.OrdinalIgnoreCase);
        var requestedPhysicalCategories = requestedCategories
            .Where(c => !string.Equals(c, ProductCategory.RepairService, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1. Process Physical Inventory Items
        var physicalRows = new List<PhysicalInventoryExportRowDto>();
        if (requestedPhysicalCategories.Count > 0)
        {
            var physicalProducts = await _db.Set<Product>().AsNoTracking()
                .Where(p => requestedPhysicalCategories.Contains(p.Category) && p.Category != ProductCategory.RepairService)
                .OrderBy(p => p.Name)
                .ToListAsync(ct);

            var invQuery = _db.Set<BranchInventory>().AsNoTracking();
            if (request.BranchIds != null && request.BranchIds.Length > 0)
            {
                invQuery = invQuery.Where(bi => request.BranchIds.Contains(bi.BranchId));
            }
            var invList = await invQuery.ToListAsync(ct);
            var stockByProduct = invList.GroupBy(bi => bi.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.StockQty));

            var imeiQuery = _db.Set<InventoryImei>().AsNoTracking()
                .Where(i => i.Status == ImeiStatus.InStock);
            if (request.BranchIds != null && request.BranchIds.Length > 0)
            {
                imeiQuery = imeiQuery.Where(i => request.BranchIds.Contains(i.BranchId));
            }
            var imeis = await imeiQuery.OrderBy(i => i.Imei).ToListAsync(ct);
            var imeisByProduct = imeis.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => g.Select(x => x.Imei).ToList());

            var purchases = await _db.Set<Purchase>().AsNoTracking()
                .OrderByDescending(pu => pu.PurchaseDate)
                .ToListAsync(ct);
            var supplierByProduct = purchases
                .Where(pu => !string.IsNullOrWhiteSpace(pu.SupplierName))
                .GroupBy(pu => pu.ProductId)
                .ToDictionary(g => g.Key, g => g.First().SupplierName);

            bool canViewCost = _scope.HasPermission(PermissionCatalog.InventoryCostView);

            foreach (var p in physicalProducts)
            {
                stockByProduct.TryGetValue(p.Id, out int quantity);
                supplierByProduct.TryGetValue(p.Id, out string? supplier);
                imeisByProduct.TryGetValue(p.Id, out var imeiList);

                string? imeiSummary = null;
                if (imeiList != null && imeiList.Count > 0)
                {
                    imeiSummary = imeiList.Count <= 10
                        ? string.Join(", ", imeiList)
                        : $"{string.Join(", ", imeiList.Take(10))}, +{imeiList.Count - 10} more";
                }

                decimal purchasePrice = canViewCost ? p.PurchasePrice : 0m;

                physicalRows.Add(new PhysicalInventoryExportRowDto(
                    Item: p.Name,
                    Category: p.Category,
                    Quantity: quantity,
                    PurchasePrice: purchasePrice,
                    SellingPrice: p.SellPrice,
                    Gst: p.GstRate,
                    Hsn: p.HsnCode,
                    Supplier: supplier,
                    ImeiSummary: imeiSummary));
            }
        }

        // 2. Process Repair Services
        var serviceRows = new List<ServiceExportRowDto>();
        if (wantsRepairService)
        {
            var serviceProducts = await _db.Set<Product>().AsNoTracking()
                .Where(p => p.Category == ProductCategory.RepairService)
                .OrderBy(p => p.Name)
                .ToListAsync(ct);

            serviceRows = serviceProducts.Select(p => new ServiceExportRowDto(
                Service: p.Name,
                Description: p.Description,
                ServiceCharge: p.SellPrice,
                Taxable: p.IsTaxable,
                Gst: p.GstRate,
                Sac: p.HsnCode
            )).ToList();
        }

        return new InventoryExportDto(physicalRows, serviceRows);
    }
}
