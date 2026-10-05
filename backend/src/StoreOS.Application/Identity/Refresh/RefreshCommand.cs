using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Refresh;

public record RefreshCommand(string RawRefreshToken) : IRequest<RefreshResult>, IAnonymousTenantRequest
{
    // Tenant is encoded in the token prefix (P-304 RefreshTokenFormat) so RLS can be scoped before the lookup.
    public Guid TenantId => RefreshTokenFormat.TryParseTenantId(RawRefreshToken, out var t) ? t : Guid.Empty;
}

public record RefreshResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string NewRawRefreshToken,
    Guid UserId,
    string DisplayName,
    Guid TenantId,
    bool AllBranches,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<string> Permissions,
    bool ReuseDetected = false);
