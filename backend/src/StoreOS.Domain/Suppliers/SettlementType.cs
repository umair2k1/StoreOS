namespace StoreOS.Domain.Suppliers;

public static class SettlementType
{
    public const string PayableDeduction = "PayableDeduction";
    public const string CashRefund = "CashRefund";
    public const string PendingCredit = "PendingCredit";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PayableDeduction,
        CashRefund,
        PendingCredit
    };

    public static bool IsValid(string type) => All.Contains(type);
}
