namespace StoreOS.Domain.Repairs;

public static class RepairJobNumberFormatter
{
    public static string Format(string branchCode, DateOnly date, int seq) =>
        $"REP-{branchCode}-{date:yyyyMMdd}-{seq:D4}";
}
