using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.ChangeStatus;

public record ChangeRepairStatusCommand(
    Guid RepairJobId,
    string NewStatus,
    string? Notes = null) : IRequest, IRequiresPermission, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
}
