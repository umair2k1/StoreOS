using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Parts.ReservePart;

public record ReservePartCommand(
    Guid RepairJobId,
    Guid? ProductId,
    string PartName,
    int Quantity,
    decimal UnitCost,
    decimal ChargeAmount) : IRequest<Guid>, IRequiresPermission, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
}
