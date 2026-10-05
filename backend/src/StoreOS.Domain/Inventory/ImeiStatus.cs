namespace StoreOS.Domain.Inventory;

public static class ImeiStatus
{
    public const string InStock = "InStock";
    public const string Sold = "Sold";
    public const string ReturnedToSupplier = "ReturnedToSupplier";
    public const string InTransit = "InTransit";
    public static readonly IReadOnlyList<string> All = new[] { InStock, Sold, ReturnedToSupplier, InTransit };
}
