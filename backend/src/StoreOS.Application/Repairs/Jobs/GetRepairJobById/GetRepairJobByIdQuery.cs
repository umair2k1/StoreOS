using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.GetRepairJobById;

public record GetRepairJobByIdQuery(Guid Id) : IRequest<RepairJobDto>, IReadOnlyRequest, IRequiresRepairsFeature;
