namespace StoreOS.Domain.Repairs;

public static class ApprovalState
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";

    public static readonly IReadOnlyList<string> All = [Pending, Approved, Rejected];

    public static bool IsValid(string state) => All.Contains(state);
}
