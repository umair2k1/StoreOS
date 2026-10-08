using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Transfers.GetPendingTransfers;

public record InventoryTransferDto(
    Guid Id,
    Guid FromBranchId,
    Guid ToBranchId,
    Guid ProductId,
    string? ProductName,
    int Qty,
    string Status,
    IReadOnlyList<string>? Imeis,
    DateTimeOffset CreatedAt);

public record GetPendingTransfersQuery : IRequest<IReadOnlyList<InventoryTransferDto>>, IRequiresPermission, IReadOnlyRequest
{
    public string RequiredPermission => "inventory:manage";
}
