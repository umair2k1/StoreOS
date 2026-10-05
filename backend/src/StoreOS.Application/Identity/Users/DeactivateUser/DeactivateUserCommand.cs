using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Users.DeactivateUser;

public record DeactivateUserCommand(Guid UserId) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "users:manage";
}
