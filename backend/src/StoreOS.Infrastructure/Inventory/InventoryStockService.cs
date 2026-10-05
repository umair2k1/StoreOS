using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Inventory;

public class InventoryStockService : IInventoryStockService
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;

    public InventoryStockService(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task ReserveAndDecrementStockAsync(Guid branchId, Guid productId, int qty,
        string movementType, string refType, string refId, CancellationToken ct)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");

        var costPrice = await _db.Set<BranchInventory>()
            .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == productId)
            .Select(bi => bi.CostPrice)
            .FirstOrDefaultAsync(ct);

        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE branch_inventory
            SET stock_qty = stock_qty - {qty}
            WHERE tenant_id = {_scope.TenantId} AND branch_id = {branchId} AND product_id = {productId}
              AND stock_qty >= {qty}", ct);

        if (affected == 0)
            throw new DomainValidationException("insufficient_stock",
                "Not enough stock available at this branch for the requested quantity.");

        var movement = InventoryMovement.Create(_scope.TenantId, branchId, productId, null,
            movementType, -qty, costPrice, refType, refId, _scope.IsAuthenticated ? _scope.UserId : null);
        await _db.Set<InventoryMovement>().AddAsync(movement, ct);
    }

    public async Task IncreaseStockAsync(Guid branchId, Guid productId, int qty, decimal? newCostPrice,
        string movementType, string refType, string refId, CancellationToken ct)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");

        var existing = await _db.Set<BranchInventory>()
            .FirstOrDefaultAsync(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId
                && bi.ProductId == productId, ct);

        decimal effectiveCost;
        if (existing is null)
        {
            var row = BranchInventory.Create(_scope.TenantId, branchId, productId);
            row.IncreaseStock(qty, newCostPrice);
            effectiveCost = row.CostPrice;
            await _db.Set<BranchInventory>().AddAsync(row, ct);
        }
        else
        {
            existing.IncreaseStock(qty, newCostPrice);
            effectiveCost = existing.CostPrice;
        }

        var movement = InventoryMovement.Create(_scope.TenantId, branchId, productId, null,
            movementType, qty, effectiveCost, refType, refId, _scope.IsAuthenticated ? _scope.UserId : null);
        await _db.Set<InventoryMovement>().AddAsync(movement, ct);
    }

    public async Task<InventoryImeiSnapshot> LoadImeiForSaleAsync(Guid branchId, string imei, CancellationToken ct)
    {
        var row = await _db.Set<InventoryImei>()
            .Where(i => i.TenantId == _scope.TenantId && i.BranchId == branchId && i.Imei == imei)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (row is null)
            throw new DomainValidationException("imei_not_found", $"IMEI {imei} was not found at this branch.");
        if (row.Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available", $"IMEI {imei} is not available (status: {row.Status}).");
        return new InventoryImeiSnapshot(row.Id, row.ProductId, row.BranchId, row.PurchasePrice, row.Status);
    }

    public async Task SellImeiAsync(Guid imeiRowId, Guid saleId, Guid saleItemId, CancellationToken ct)
    {
        var row = await _db.Set<InventoryImei>().FromSqlInterpolated($@"
            SELECT * FROM inventory_imeis WHERE id = {imeiRowId} AND tenant_id = {_scope.TenantId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (row is null) throw new DomainValidationException("imei_not_found", "IMEI record not found.");
        row.MarkSold(saleId, saleItemId);
    }

    public async Task RestoreImeiToStockAsync(Guid imeiRowId, CancellationToken ct)
    {
        var row = await _db.Set<InventoryImei>().FromSqlInterpolated($@"
            SELECT * FROM inventory_imeis WHERE id = {imeiRowId} AND tenant_id = {_scope.TenantId} FOR UPDATE")
            .FirstOrDefaultAsync(ct);
        if (row is null) throw new DomainValidationException("imei_not_found", "IMEI record not found.");
        row.RestoreToStock();
    }
}
