using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Users.AssignRole;

public record AssignRoleCommand(
    Guid UserId,
    Guid RoleId,
    Guid? BranchId = null) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "users:manage";
}
