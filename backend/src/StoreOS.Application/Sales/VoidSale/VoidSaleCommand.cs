using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.VoidSale;

public record VoidSaleCommand(Guid SaleId, string Reason) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "sales:void";
}
