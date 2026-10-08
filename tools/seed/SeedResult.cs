namespace StoreOS.Seed;

public record SeedResult
{
    public bool Success { get; init; }
    public bool Skipped { get; init; }
    public string? Message { get; init; }
    public Guid TenantId { get; init; }
    public Guid BranchB01Id { get; init; }
    public Guid BranchB02Id { get; init; }
    public int ProductCount { get; init; }
    public int InventoryCount { get; init; }
    public int ImeiCount { get; init; }
    public int CustomerCount { get; init; }
    public int SupplierCount { get; init; }
    public int SaleCount { get; init; }
    public int ExpenseCount { get; init; }
    public int LiabilityCount { get; init; }
    public int RepairJobCount { get; init; }
}
