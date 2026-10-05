using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Password.AdminResetPassword;

public record AdminResetPasswordCommand(Guid UserId, string NewTemporaryPassword)
    : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "users:manage";
}
