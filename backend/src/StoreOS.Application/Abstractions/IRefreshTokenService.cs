using StoreOS.Domain.Identity;

namespace StoreOS.Application.Abstractions;

public interface IRefreshTokenService
{
    Task<(string rawToken, RefreshToken record)> IssueAsync(Guid tenantId, Guid userId, Guid familyId, CancellationToken ct);
}
