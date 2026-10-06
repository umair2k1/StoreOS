using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Customers.Crud.UpdateCustomerProfile;

public class UpdateCustomerProfileHandler : IRequestHandler<UpdateCustomerProfileCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public UpdateCustomerProfileHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(UpdateCustomerProfileCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new DomainValidationException("customer_name_required", "Customer name is required.");
        }

        var customer = await _db.Set<Customer>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == request.Id, ct);

        if (customer == null)
        {
            throw new DomainValidationException("customer_not_found", $"Customer '{request.Id}' was not found.");
        }

        customer.UpdateProfile(request.Name, request.Phone?.Trim(), request.Gstin?.Trim());
        await _db.SaveChangesAsync(ct);
    }
}
