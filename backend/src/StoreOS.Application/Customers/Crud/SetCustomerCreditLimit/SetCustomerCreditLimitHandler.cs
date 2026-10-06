using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Customers.Crud.SetCustomerCreditLimit;

public class SetCustomerCreditLimitHandler : IRequestHandler<SetCustomerCreditLimitCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public SetCustomerCreditLimitHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(SetCustomerCreditLimitCommand request, CancellationToken ct)
    {
        var customer = await _db.Set<Customer>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == request.Id, ct);

        if (customer == null)
        {
            throw new DomainValidationException("customer_not_found", $"Customer '{request.Id}' was not found.");
        }

        customer.SetCreditLimit(request.CreditLimit);
        await _db.SaveChangesAsync(ct);
    }
}
