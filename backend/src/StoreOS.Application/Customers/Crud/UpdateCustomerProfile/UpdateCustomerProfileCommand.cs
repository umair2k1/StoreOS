using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Crud.UpdateCustomerProfile;

public record UpdateCustomerProfileCommand(
    Guid Id,
    string Name,
    string? Phone = null,
    string? Gstin = null) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "pos:checkout";
}
