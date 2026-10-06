using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Crud.SetCustomerCreditLimit;

public record SetCustomerCreditLimitCommand(
    Guid Id,
    decimal? CreditLimit) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "settings:manage";
}
