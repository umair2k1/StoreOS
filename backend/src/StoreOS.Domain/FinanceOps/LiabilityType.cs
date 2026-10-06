namespace StoreOS.Domain.FinanceOps;

public static class LiabilityType
{
    public const string Loan = "Loan";
    public const string CreditCard = "CreditCard";
    public const string Financing = "Financing";
    public const string Investment = "Investment";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Loan,
        CreditCard,
        Financing,
        Investment
    };

    public static bool IsValid(string type) => All.Contains(type);
}
