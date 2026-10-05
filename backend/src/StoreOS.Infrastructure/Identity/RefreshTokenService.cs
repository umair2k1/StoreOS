using System.Security.Cryptography;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity;
using StoreOS.Domain.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Identity;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly StoreOsDbContext _db;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public RefreshTokenService(StoreOsDbContext db) => _db = db;

    public async Task<(string rawToken, RefreshToken record)> IssueAsync(
        Guid tenantId, Guid userId, Guid familyId, CancellationToken ct)
    {
        var rawToken = RefreshTokenFormat.Compose(tenantId, RandomNumberGenerator.GetBytes(32));
        var hash = RefreshTokenFormat.Hash(rawToken);
        var record = RefreshToken.IssueNew(userId, hash, familyId, DateTimeOffset.UtcNow.Add(Lifetime));
        await _db.Set<RefreshToken>().AddAsync(record, ct);
        return (rawToken, record);
    }
}
