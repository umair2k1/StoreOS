using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Users.AssignBranch;

public record AssignBranchCommand(
    Guid UserId,
    Guid BranchId,
    bool IsDefault = false) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "users:manage";
}
