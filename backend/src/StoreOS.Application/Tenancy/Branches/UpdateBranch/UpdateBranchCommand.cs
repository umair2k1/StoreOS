using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Tenancy.Branches.UpdateBranch;

public record UpdateBranchCommand(
    Guid Id,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Gstin,
    bool IsActive) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "branches:manage";
}
