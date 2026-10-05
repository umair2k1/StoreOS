using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public class TenantJobRunner : ITenantJobRunner
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantJobRunner(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
        accessScope.UseSystemContext(tenantId);

        var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await work(scope.ServiceProvider, ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch { /* transaction already aborted */ }
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
