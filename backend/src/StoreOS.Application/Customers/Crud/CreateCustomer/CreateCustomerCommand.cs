using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Crud.CreateCustomer;

public record CreateCustomerCommand(
    string Name,
    string? Phone = null,
    string? Gstin = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "pos:checkout";
}
