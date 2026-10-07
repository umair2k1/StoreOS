using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Jobs;

public class IdempotencyKeyPurgeJob
{
    private readonly StoreOsDbContext _db;
    private readonly ITenantJobRunner _jobRunner;
    private readonly ILogger<IdempotencyKeyPurgeJob> _logger;

    public IdempotencyKeyPurgeJob(
        StoreOsDbContext db,
        ITenantJobRunner jobRunner,
        ILogger<IdempotencyKeyPurgeJob> logger)
    {
        _db = db;
        _jobRunner = jobRunner;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().Where(t => t.IsActive).Select(t => t.Id).ToListAsync(ct);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);

        foreach (var tenantId in tenants)
        {
            try
            {
                await _jobRunner.RunAsync(tenantId, async (sp, token) =>
                {
                    var scopedDb = sp.GetRequiredService<StoreOsDbContext>();
                    await scopedDb.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM idempotency_keys WHERE tenant_id = {tenantId} AND created_at < {cutoff}", token);
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to purge old idempotency keys for tenant {TenantId}", tenantId);
            }
        }
    }
}
