using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public class IdempotencyStore : IIdempotencyStore
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;

    public IdempotencyStore(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IdempotencyBegin> BeginAsync(string key, string scope, string requestHash, CancellationToken ct)
    {
        var tenantId = _scope.TenantId;
        var userId = _scope.UserId;

        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO idempotency_keys (tenant_id, user_id, key, endpoint, request_hash)
            VALUES ({tenantId}, {userId}, {key}, {scope}, {requestHash})
            ON CONFLICT (tenant_id, user_id, endpoint, key) DO NOTHING;
        ", ct);

        if (rows > 0)
        {
            return new IdempotencyBegin(IdempotencyOutcome.Started, null);
        }

        var existing = await _db.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.UserId == userId && r.Endpoint == scope && r.Key == key, ct);

        if (existing == null)
        {
            return new IdempotencyBegin(IdempotencyOutcome.InProgress, null);
        }

        if (existing.RequestHash != requestHash)
        {
            return new IdempotencyBegin(IdempotencyOutcome.Mismatch, null);
        }

        if (existing.ResponseBodyJson == null)
        {
            return new IdempotencyBegin(IdempotencyOutcome.InProgress, null);
        }

        return new IdempotencyBegin(IdempotencyOutcome.Replay, existing.ResponseBodyJson);
    }

    public async Task CompleteAsync(string key, string scope, string responseJson, CancellationToken ct)
    {
        var tenantId = _scope.TenantId;
        var userId = _scope.UserId;

        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE idempotency_keys
            SET response_status = 200, response_body = {responseJson}::jsonb
            WHERE tenant_id = {tenantId} AND user_id = {userId} AND endpoint = {scope} AND key = {key};
        ", ct);
    }
}
