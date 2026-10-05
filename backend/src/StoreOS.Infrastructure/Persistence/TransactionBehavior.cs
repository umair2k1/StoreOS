using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IServiceProvider _services;
    private readonly IAccessScope _scope;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(IServiceProvider services, IAccessScope scope,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    {
        _services = services;
        _scope = scope;
        _logger = logger;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IAnonymousTenantRequest anon)
        {
            if (!_scope.IsAuthenticated)
            {
                if (anon.TenantId == Guid.Empty)
                    throw new DomainException("tenant_required", "The tenant could not be determined for this request.");
                _scope.UseAnonymousTenant(anon.TenantId);
            }
        }

        // Requests without any tenant context never touch RLS-protected tables (e.g. tenant lookup by host).
        if (!_scope.HasTenantContext) return await next();

        DbContext db = request is IReportingRequest
            ? _services.GetRequiredService<ReportingDbContext>()
            : _services.GetRequiredService<StoreOsDbContext>();

        if (db.Database.CurrentTransaction is not null) return await next(); // nested Send(): join the outer transaction

        var (isolation, readOnly) = request switch
        {
            IReportingRequest => (IsolationLevel.RepeatableRead, true),
            ISerializableRequest => (IsolationLevel.Serializable, false),
            IReadOnlyRequest => (IsolationLevel.ReadCommitted, true),
            _ => (IsolationLevel.RepeatableRead, false)
        };

        await using var tx = await db.Database.BeginTransactionAsync(isolation, ct); // interceptor sets app.* here
        try
        {
            if (readOnly) await db.Database.ExecuteSqlRawAsync("SET LOCAL transaction_read_only = on", ct);
            var response = await next();
            await tx.CommitAsync(ct);
            return response;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackEx) { _logger.LogDebug(rollbackEx, "Rollback after failure was not possible (transaction already aborted)."); }
            db.ChangeTracker.Clear(); // a retry (P-511) must never see entities from the failed attempt
            throw;
        }
    }
}
