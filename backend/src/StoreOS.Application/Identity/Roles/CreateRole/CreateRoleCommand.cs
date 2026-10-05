using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Roles.CreateRole;

public record CreateRoleCommand(string Name, IReadOnlyList<string> PermissionCodes)
    : IRequest<Guid>, IHighImpactOperation
{
    public string RequiredPermission => "users:manage";
}
