using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Deliver;

public record DeliverRepairJobCommand(Guid RepairJobId, int? WarrantyDaysOverride = null)
    : IRequest, IRequiresPermission, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
}
