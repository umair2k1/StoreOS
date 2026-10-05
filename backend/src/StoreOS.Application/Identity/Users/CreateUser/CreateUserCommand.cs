using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Users.CreateUser;

public record CreateUserCommand(
    string Email,
    string DisplayName,
    string Password,
    Guid? InitialRoleId = null,
    Guid? InitialBranchId = null) : IRequest<Guid>, IHighImpactOperation
{
    public string RequiredPermission => "users:manage";
}
