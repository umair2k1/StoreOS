namespace StoreOS.Domain.Sales;

public static class PaymentMode
{
    public const string Cash = "Cash";
    public const string UPI = "UPI";
    public const string Card = "Card";
    public const string Credit = "Credit";
    public const string Finance = "Finance";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Cash,
        UPI,
        Card,
        Credit,
        Finance
    };
}
