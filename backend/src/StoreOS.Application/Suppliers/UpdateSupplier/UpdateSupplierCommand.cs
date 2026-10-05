using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.UpdateSupplier;

public record UpdateSupplierCommand(
    Guid Id,
    string Name,
    string? Phone = null,
    string? Address = null,
    string? BrandOrCategory = null,
    string? Notes = null) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
