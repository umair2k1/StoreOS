using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Services.DeactivateRepairService;

public record DeactivateRepairServiceCommand(Guid Id) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
