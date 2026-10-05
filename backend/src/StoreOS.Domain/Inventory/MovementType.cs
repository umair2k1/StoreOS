namespace StoreOS.Domain.Inventory;

public static class MovementType
{
    public const string Purchase = "Purchase";
    public const string Sale = "Sale";
    public const string SaleVoid = "SaleVoid";
    public const string SaleRefund = "SaleRefund";
    public const string SupplierReturn = "SupplierReturn";
    public const string RepairConsumed = "RepairConsumed";
    public const string RepairPartReturned = "RepairPartReturned";
    public const string Adjustment = "Adjustment";
    public const string TransferOut = "TransferOut";
    public const string TransferIn = "TransferIn";
    public static readonly IReadOnlyList<string> All = new[]
    {
        Purchase, Sale, SaleVoid, SaleRefund, SupplierReturn, RepairConsumed,
        RepairPartReturned, Adjustment, TransferOut, TransferIn
    };
}
