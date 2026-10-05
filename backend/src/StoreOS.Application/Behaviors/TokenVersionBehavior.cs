using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public class TokenVersionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAccessScope _scope;
    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public TokenVersionBehavior(IAccessScope scope, IAppDbContext db, IMemoryCache cache)
    {
        _scope = scope;
        _db = db;
        _cache = cache;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_scope.IsAuthenticated) return await next();

        var cacheKey = $"token_version:{_scope.UserId}";
        var currentVersion = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var user = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
                .Where(u => u.Id == _scope.UserId)
                .Select(u => (int?)u.TokenVersion)
                .FirstOrDefaultAsync(cancellationToken);
            return user;
        });

        if (currentVersion is null || currentVersion.Value != _scope.TokenVersion)
            throw new DomainException("token_version_stale",
                "Your session is no longer valid. Please log in again.");

        return await next();
    }
}
