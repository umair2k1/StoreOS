using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Services.UpdateRepairService;

public record UpdateRepairServiceCommand(
    Guid Id,
    string? Name = null,
    string? Description = null,
    decimal? StandardCharge = null,
    bool? Taxable = null,
    decimal? GstRate = null,
    string? SacCode = null,
    int? EstimatedDurationMinutes = null,
    int? WarrantyDays = null,
    bool? IsActive = null) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
