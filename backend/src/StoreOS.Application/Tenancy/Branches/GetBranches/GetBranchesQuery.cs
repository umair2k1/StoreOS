using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Tenancy.Branches.GetBranches;

public record GetBranchesQuery : IRequest<IReadOnlyList<BranchDto>>, IReadOnlyRequest;
