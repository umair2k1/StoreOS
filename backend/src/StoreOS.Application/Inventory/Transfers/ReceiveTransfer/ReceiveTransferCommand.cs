using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Transfers.ReceiveTransfer;

public record ReceiveTransferCommand(Guid TransferId) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "inventory:manage";
}
