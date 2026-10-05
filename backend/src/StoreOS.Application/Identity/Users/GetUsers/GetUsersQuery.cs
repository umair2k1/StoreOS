using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Users.GetUsers;

public record GetUsersQuery : IRequest<IReadOnlyList<UserDto>>, IRequiresPermission, IReadOnlyRequest
{
    public string RequiredPermission => "users:manage";
}
