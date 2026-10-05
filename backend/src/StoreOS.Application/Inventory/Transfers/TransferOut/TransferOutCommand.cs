using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Transfers.TransferOut;

public record TransferOutCommand(
    Guid ToBranchId,
    Guid ProductId,
    int Qty,
    IReadOnlyList<string>? ImeiStrings = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "inventory:manage";
}
