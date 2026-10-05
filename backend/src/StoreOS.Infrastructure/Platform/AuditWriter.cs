using System.Text.Json;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Platform;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Platform;

public class AuditWriter : IAuditWriter
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    private readonly ICorrelationContextAccessor _correlation;

    public AuditWriter(StoreOsDbContext db, IAccessScope scope, ICorrelationContextAccessor correlation)
    {
        _db = db;
        _scope = scope;
        _correlation = correlation;
    }

    public async Task WriteAsync(Guid? branchId, string action, string entityType, string entityId,
        object? changes, CancellationToken ct = default)
    {
        var changesJson = changes is null ? null : JsonSerializer.Serialize(changes);
        var entry = AuditLogEntry.Create(_scope.TenantId, branchId, _correlation.CorrelationId,
            _scope.UserId, action, entityType, entityId, changesJson);
        await _db.AuditLogEntries.AddAsync(entry, ct);
    }
}
