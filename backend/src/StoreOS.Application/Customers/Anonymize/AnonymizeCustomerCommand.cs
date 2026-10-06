using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Anonymize;

public record AnonymizeCustomerCommand(Guid CustomerId) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "settings:manage";
}
