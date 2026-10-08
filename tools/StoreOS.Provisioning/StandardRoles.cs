using StoreOS.Domain.Identity;

namespace StoreOS.Provisioning;

public static class StandardRoles
{
    public const string Owner = "Owner";
    public const string RegionalManager = "RegionalManager";
    public const string BranchManager = "BranchManager";
    public const string Cashier = "Cashier";
    public const string Technician = "Technician";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Owner,
        RegionalManager,
        BranchManager,
        Cashier,
        Technician
    };

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RolePermissions =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Owner] = PermissionCatalog.All,
            [RegionalManager] = new[]
            {
                PermissionCatalog.SalesReadAnyBranch,
                PermissionCatalog.InventoryCostView,
                PermissionCatalog.ReportsProfitView
            },
            [BranchManager] = new[]
            {
                PermissionCatalog.PosCheckout,
                PermissionCatalog.SalesDiscountApply,
                PermissionCatalog.SalesCreditOverride,
                PermissionCatalog.InventoryCostView,
                PermissionCatalog.ReportsProfitView,
                PermissionCatalog.InventoryManage,
                PermissionCatalog.PurchasesManage,
                PermissionCatalog.InventoryAdjustments,
                PermissionCatalog.RepairsManage
            },
            [Cashier] = new[]
            {
                PermissionCatalog.PosCheckout
            },
            [Technician] = new[]
            {
                PermissionCatalog.RepairsManage
            }
        };
}
