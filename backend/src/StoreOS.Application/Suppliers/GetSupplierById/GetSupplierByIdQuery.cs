using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.GetSupplierById;

public record GetSupplierByIdQuery(Guid Id) : IRequest<SupplierDto>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
