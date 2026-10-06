using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Customers.Crud.CreateCustomer;

public class CreateCustomerHandler : IRequestHandler<CreateCustomerCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public CreateCustomerHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(CreateCustomerCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("customer_name_required", "Customer name is required.");
        }

        var customer = Customer.Create(
            _scope.TenantId,
            request.Name,
            request.Phone?.Trim(),
            request.Gstin?.Trim());

        await _db.Set<Customer>().AddAsync(customer, ct);
        await _db.SaveChangesAsync(ct);

        return customer.Id;
    }
}
