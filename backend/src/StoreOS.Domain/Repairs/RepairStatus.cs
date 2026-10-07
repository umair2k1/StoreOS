namespace StoreOS.Domain.Repairs;

public static class RepairStatus
{
    public const string Received = "Received";
    public const string Diagnosing = "Diagnosing";
    public const string WaitingForApproval = "WaitingForApproval";
    public const string WaitingForParts = "WaitingForParts";
    public const string InRepair = "InRepair";
    public const string ReadyForPickup = "ReadyForPickup";
    public const string Delivered = "Delivered";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Received,
        Diagnosing,
        WaitingForApproval,
        WaitingForParts,
        InRepair,
        ReadyForPickup,
        Delivered,
        Cancelled
    };

    public static bool IsValid(string status) => All.Contains(status);

    public static bool IsActive(string status) => status != Delivered && status != Cancelled;
}
