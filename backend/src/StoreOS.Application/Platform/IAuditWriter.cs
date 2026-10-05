namespace StoreOS.Application.Platform;

public interface IAuditWriter
{
    Task WriteAsync(Guid? branchId, string action, string entityType, string entityId,
        object? changes, CancellationToken ct = default);
}
