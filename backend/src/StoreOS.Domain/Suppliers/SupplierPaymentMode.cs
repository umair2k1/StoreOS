namespace StoreOS.Domain.Suppliers;

public static class SupplierPaymentMode
{
    public const string Cash = "Cash";
    public const string UPI = "UPI";
    public const string BankTransfer = "BankTransfer";
    public const string Cheque = "Cheque";
    public const string Credit = "Credit";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Cash, UPI, BankTransfer, Cheque, Credit
    };

    public static bool IsValid(string mode) => All.Contains(mode);
}
