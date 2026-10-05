namespace StoreOS.Domain.Customers;

public static class CustomerLedgerEntryType
{
    public const string Invoice = "Invoice";
    public const string Payment = "Payment";
    public const string InvoiceVoid = "InvoiceVoid";
    public const string Refund = "Refund";
    public const string OpeningBalance = "OpeningBalance";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Invoice,
        Payment,
        InvoiceVoid,
        Refund,
        OpeningBalance
    };
}
