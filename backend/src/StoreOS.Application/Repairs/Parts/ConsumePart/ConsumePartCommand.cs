using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Parts.ConsumePart;

public record ConsumePartCommand(
    Guid RepairJobId,
    Guid PartId) : IRequest, IRequiresPermission, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
}
