namespace StoreOS.Domain.Sales;

public static class InvoiceType
{
    public const string TaxInvoice = "TaxInvoice";
    public const string Estimate = "Estimate";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        TaxInvoice,
        Estimate
    };
}
