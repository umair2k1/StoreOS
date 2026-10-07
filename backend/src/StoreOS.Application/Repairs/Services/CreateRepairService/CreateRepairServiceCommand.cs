using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Services.CreateRepairService;

public record CreateRepairServiceCommand(
    string Name,
    string? Description,
    decimal StandardCharge,
    bool Taxable,
    decimal? GstRate,
    string? SacCode,
    int? EstimatedDurationMinutes,
    int? WarrantyDays)
    : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
