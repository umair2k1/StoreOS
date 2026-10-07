using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.CreateRepairJob;

public record CreateRepairJobCommand(
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerAddress,
    string? DeviceBrand,
    string? DeviceModel,
    string? DeviceType,
    string? SerialNumber,
    string? Imei1,
    string? Imei2,
    string? DeviceCondition,
    string? AccessoriesReceived,
    string? ProblemDescription,
    string? CustomerNotes,
    decimal? EstimatedCost = null,
    decimal? AdvancePaid = null,
    DateTimeOffset? ExpectedCompletion = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
