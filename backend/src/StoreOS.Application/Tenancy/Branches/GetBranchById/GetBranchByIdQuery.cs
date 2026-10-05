using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Tenancy.Branches.GetBranchById;

public record GetBranchByIdQuery(Guid Id) : IRequest<BranchDto>, IReadOnlyRequest;
