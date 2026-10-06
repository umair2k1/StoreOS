namespace StoreOS.Domain.Suppliers;

public static class SupplierLedgerEntryType
{
    public const string Bill = "Bill";
    public const string Payment = "Payment";
    public const string OpeningBalance = "OpeningBalance";
    public const string ReturnCredit = "ReturnCredit";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Bill, Payment, OpeningBalance, ReturnCredit
    };

    public static bool IsValid(string type) => All.Contains(type);
}
