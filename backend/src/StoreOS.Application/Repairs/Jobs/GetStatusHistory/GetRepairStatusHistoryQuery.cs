using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.GetStatusHistory;

public record GetRepairStatusHistoryQuery(Guid RepairJobId)
    : IRequest<IReadOnlyList<RepairStatusHistoryDto>>, IReadOnlyRequest, IRequiresRepairsFeature;
