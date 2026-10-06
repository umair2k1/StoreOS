namespace StoreOS.Domain.Suppliers;

public static class SupplierTransactionType
{
    public const string Bill = "Bill";
    public const string Payment = "Payment";
    public const string OpeningBalance = "OpeningBalance";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Bill, Payment, OpeningBalance
    };

    public static bool IsValid(string type) => All.Contains(type);
}
