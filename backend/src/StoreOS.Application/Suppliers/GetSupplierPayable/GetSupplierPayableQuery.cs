using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.GetSupplierPayable;

public record GetSupplierPayableQuery(Guid SupplierId) : IRequest<SupplierPayableDto>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
