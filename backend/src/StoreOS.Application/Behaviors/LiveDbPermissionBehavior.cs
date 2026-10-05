using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Behaviors;

public class LiveDbPermissionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAccessScope _scope;
    private readonly IAppDbContext _db;

    public LiveDbPermissionBehavior(IAccessScope scope, IAppDbContext db)
    {
        _scope = scope;
        _db = db;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IHighImpactOperation highImpact) return await next();

        if (!_scope.IsAuthenticated)
            throw new DomainException("unauthenticated", "Authentication is required.");

        // NO CACHE. Direct DB query, every time, inside the ambient transaction.
        var roleIds = await _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
            .Where(a => a.UserId == _scope.UserId)
            .Select(a => a.RoleId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var livePermissions = await _db.Set<RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId) && rp.PermissionCode == highImpact.RequiredPermission)
            .AnyAsync(cancellationToken);

        if (!livePermissions)
            throw new DomainException("forbidden",
                $"Missing required permission (live check): {highImpact.RequiredPermission}");

        // Also re-check the user is still active (covers deactivation mid-session even
        // faster than the token_version cache would).
        var stillActive = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
            .Where(u => u.Id == _scope.UserId).Select(u => u.IsActive).FirstOrDefaultAsync(cancellationToken);
        if (!stillActive)
            throw new DomainException("forbidden", "Account is no longer active.");

        return await next();
    }
}
