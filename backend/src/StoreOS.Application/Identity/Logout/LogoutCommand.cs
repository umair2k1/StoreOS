using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Logout;

public record LogoutCommand(string RawRefreshToken) : IRequest, IAnonymousTenantRequest
{
    public Guid TenantId => RefreshTokenFormat.TryParseTenantId(RawRefreshToken, out var t) ? t : Guid.Empty;
}
