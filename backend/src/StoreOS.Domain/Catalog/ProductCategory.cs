namespace StoreOS.Domain.Catalog;

public static class ProductCategory
{
    public const string NewPhone = "NewPhone";
    public const string UsedPhone = "UsedPhone";
    public const string Accessory = "Accessory";
    public const string RepairService = "RepairService";
    public static readonly IReadOnlyList<string> All = new[] { NewPhone, UsedPhone, Accessory, RepairService };
    public static bool IsValid(string category) => All.Contains(category);
    public static bool IsPhysical(string category) => category != RepairService;
}
