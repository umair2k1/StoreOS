namespace StoreOS.Application.Inventory;

public interface IInventoryStockService
{
    Task ReserveAndDecrementStockAsync(Guid branchId, Guid productId, int qty, string movementType,
        string refType, string refId, CancellationToken ct);

    Task IncreaseStockAsync(Guid branchId, Guid productId, int qty, decimal? newCostPrice,
        string movementType, string refType, string refId, CancellationToken ct);

    Task SellImeiAsync(Guid imeiRowId, Guid saleId, Guid saleItemId, CancellationToken ct);
    Task RestoreImeiToStockAsync(Guid imeiRowId, CancellationToken ct);
    Task<InventoryImeiSnapshot> LoadImeiForSaleAsync(Guid branchId, string imei, CancellationToken ct);
}
