namespace StoreOS.Application.Repairs.Jobs.GetStatusHistory;

public record RepairStatusHistoryDto(
    long Id,
    Guid RepairJobId,
    string? FromStatus,
    string ToStatus,
    string? Notes,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt);
