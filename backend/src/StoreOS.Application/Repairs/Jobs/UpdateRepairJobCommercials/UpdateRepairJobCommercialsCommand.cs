using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJobCommercials;

public record UpdateRepairJobCommercialsCommand(
    Guid Id,
    decimal? EstimatedCost,
    decimal? ApprovedCost,
    decimal? PartsCost,
    decimal? LaborCharge,
    decimal? Discount,
    decimal? Tax,
    decimal? FinalTotal,
    decimal? AdvancePaid) : IRequest, IRequiresPermission, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
}
