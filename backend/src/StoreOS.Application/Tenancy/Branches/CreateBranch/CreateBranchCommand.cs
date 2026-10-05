using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Tenancy.Branches.CreateBranch;

public record CreateBranchCommand(
    string Code,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Gstin) : IRequest<Guid>, IHighImpactOperation
{
    public string RequiredPermission => "branches:manage";
}
