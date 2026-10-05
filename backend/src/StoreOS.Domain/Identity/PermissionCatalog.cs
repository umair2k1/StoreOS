namespace StoreOS.Domain.Identity;

public static class PermissionCatalog
{
    public const string PosCheckout = "pos:checkout";
    public const string SalesDiscountApply = "sales:discount:apply";
    public const string SalesVoid = "sales:void";
    public const string SalesRefund = "sales:refund";
    public const string SalesCreditOverride = "sales:credit:override";
    public const string SalesReadAnyBranch = "sales:read:any-branch";
    public const string InventoryCostView = "inventory:cost:view";
    public const string ReportsProfitView = "reports:profit:view";
    public const string InventoryManage = "inventory:manage";
    public const string PurchasesManage = "purchases:manage";
    public const string InventoryAdjustments = "inventory:adjustments";
    public const string ExpensesManage = "expenses:manage";
    public const string LiabilitiesManage = "liabilities:manage";
    public const string RepairsManage = "repairs:manage";
    public const string SettingsManage = "settings:manage";
    public const string UsersManage = "users:manage";
    public const string BranchesManage = "branches:manage";

    public static readonly IReadOnlyList<string> All = new[]
    {
        PosCheckout, SalesDiscountApply, SalesVoid, SalesRefund, SalesCreditOverride,
        SalesReadAnyBranch, InventoryCostView, ReportsProfitView, InventoryManage,
        PurchasesManage, InventoryAdjustments, ExpensesManage, LiabilitiesManage,
        RepairsManage, SettingsManage, UsersManage, BranchesManage
    };

    public static bool IsValid(string code) => All.Contains(code);
}
