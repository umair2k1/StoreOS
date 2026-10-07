namespace StoreOS.Domain.Repairs;

public class RepairJobStatusHistory
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RepairJobId { get; private set; }
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = default!;
    public string? Notes { get; private set; }
    public Guid? ChangedBy { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    private RepairJobStatusHistory() { }

    public static RepairJobStatusHistory Create(
        Guid tenantId,
        Guid repairJobId,
        string? fromStatus,
        string toStatus,
        string? notes,
        Guid? changedBy)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        if (repairJobId == Guid.Empty) throw new ArgumentException("Repair job ID is required.", nameof(repairJobId));
        if (string.IsNullOrWhiteSpace(toStatus)) throw new ArgumentException("ToStatus is required.", nameof(toStatus));

        return new RepairJobStatusHistory
        {
            TenantId = tenantId,
            RepairJobId = repairJobId,
            FromStatus = fromStatus,
            ToStatus = toStatus.Trim(),
            Notes = notes?.Trim(),
            ChangedBy = changedBy,
            ChangedAt = DateTimeOffset.UtcNow
        };
    }

    public static RepairJobStatusHistory Rehydrate(
        long id, Guid tenantId, Guid repairJobId, string? fromStatus, string toStatus,
        string? notes, Guid? changedBy, DateTimeOffset changedAt)
    {
        return new RepairJobStatusHistory
        {
            Id = id,
            TenantId = tenantId,
            RepairJobId = repairJobId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Notes = notes,
            ChangedBy = changedBy,
            ChangedAt = changedAt
        };
    }
}
