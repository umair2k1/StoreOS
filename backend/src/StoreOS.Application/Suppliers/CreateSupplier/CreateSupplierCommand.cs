using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.CreateSupplier;

public record CreateSupplierCommand(
    string Name,
    string? Phone = null,
    string? Address = null,
    string? BrandOrCategory = null,
    string? Notes = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
