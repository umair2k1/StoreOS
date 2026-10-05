using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.GetSuppliers;

public record GetSuppliersQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<SupplierDto>>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
