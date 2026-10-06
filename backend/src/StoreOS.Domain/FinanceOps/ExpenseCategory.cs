namespace StoreOS.Domain.FinanceOps;

public static class ExpenseCategory
{
    public const string Rent = "Rent";
    public const string Utilities = "Utilities";
    public const string Salaries = "Salaries";
    public const string Miscellaneous = "Miscellaneous";

    public static readonly IReadOnlyList<string> Suggested = new[]
    {
        Rent,
        Utilities,
        Salaries,
        Miscellaneous
    };
    // NOTE: this is a SUGGESTION list only, not a closed enum — any non-empty string is a
    // valid category, per Requirements §18.3's extensibility requirement.
}
