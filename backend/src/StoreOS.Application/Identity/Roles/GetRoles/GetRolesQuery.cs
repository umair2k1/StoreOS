using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Roles.GetRoles;

public record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>, IRequiresPermission, IReadOnlyRequest
{
    public string RequiredPermission => "users:manage";
}
