namespace StoreOS.Domain.Repairs;

public static class RepairPartStatus
{
    public const string Reserved = "Reserved";
    public const string Consumed = "Consumed";
    public const string Returned = "Returned";

    public static readonly IReadOnlyList<string> All = new[] { Reserved, Consumed, Returned };

    public static bool IsValid(string status) => All.Contains(status);
}
