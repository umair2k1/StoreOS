namespace StoreOS.Domain.Sales;

public static class SaleStatus
{
    public const string Active = "Active";
    public const string Voided = "Voided";
    public const string Refunded = "Refunded";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Active,
        Voided,
        Refunded
    };
}
