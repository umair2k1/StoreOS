using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Approval;

public record RecordApprovalCommand(Guid RepairJobId, string ApprovalState)
    : IRequest, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
