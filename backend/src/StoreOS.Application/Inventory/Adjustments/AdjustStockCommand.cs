using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Adjustments;

public record AdjustStockCommand(Guid ProductId, int QtyDelta, string Reason)
    : IRequest<AdjustStockResult>, IHighImpactOperation
{
    public string RequiredPermission => "inventory:adjustments";
}

public record AdjustStockResult(int NewStockQty);
