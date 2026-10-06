using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.RefundSale;

public record RefundSaleCommand(
    Guid SaleId,
    IReadOnlyList<Guid>? SaleItemIds,
    string Reason,
    decimal RefundAmount
) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "sales:refund";
}
