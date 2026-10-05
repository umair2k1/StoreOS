namespace StoreOS.Application.Inventory;

public record InventoryImeiSnapshot(Guid Id, Guid ProductId, Guid BranchId, decimal? PurchasePrice, string Status);
