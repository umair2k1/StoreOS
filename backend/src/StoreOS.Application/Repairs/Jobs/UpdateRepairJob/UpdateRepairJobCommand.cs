using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJob;

public record UpdateRepairJobCommand(
    Guid Id,
    string? CustomerNotes = null,
    string? ProblemDescription = null,
    string? DeviceCondition = null,
    string? AccessoriesReceived = null,
    DateTimeOffset? ExpectedCompletion = null) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
