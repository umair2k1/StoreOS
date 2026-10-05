using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Login;

public record LoginCommand(string Email, string Password, Guid TenantId)
    : IRequest<LoginResult>, IAnonymousTenantRequest;

public record LoginResult(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    string DisplayName,
    Guid TenantId,
    bool AllBranches,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<string> Permissions,
    string RawRefreshToken);
