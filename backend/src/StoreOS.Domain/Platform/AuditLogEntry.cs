namespace StoreOS.Domain.Platform;

public class AuditLogEntry
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public string EntityId { get; private set; } = default!;
    public string? ChangesJson { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Create(Guid tenantId, Guid? branchId, string? correlationId,
        Guid? userId, string action, string entityType, string entityId, string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("EntityType is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("EntityId is required.", nameof(entityId));
        return new AuditLogEntry
        {
            TenantId = tenantId,
            BranchId = branchId,
            CorrelationId = correlationId,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ChangesJson = changesJson,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
