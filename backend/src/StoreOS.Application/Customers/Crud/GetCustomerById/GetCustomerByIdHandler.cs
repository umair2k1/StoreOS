using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Customers.Crud.GetCustomerById;

public class GetCustomerByIdHandler : IRequestHandler<GetCustomerByIdQuery, CustomerDetailDto>
{
    private readonly IAppDbContext _db;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAccessScope _scope;

    public GetCustomerByIdHandler(
        IAppDbContext db,
        ICustomerLedgerService ledgerService,
        IAccessScope scope)
    {
        _db = db;
        _ledgerService = ledgerService;
        _scope = scope;
    }

    public async Task<CustomerDetailDto> Handle(GetCustomerByIdQuery request, CancellationToken ct)
    {
        var customer = await _db.Set<Customer>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == request.Id, ct);

        if (customer == null)
        {
            throw new DomainValidationException("customer_not_found", $"Customer '{request.Id}' was not found.");
        }

        var balance = await _ledgerService.GetConsolidatedBalanceAsync(customer.Id, ct);

        return new CustomerDetailDto(
            customer.Id,
            customer.Name,
            customer.Phone,
            customer.Gstin,
            customer.TotalPurchases,
            customer.CreditLimit,
            balance,
            customer.CreatedAt);
    }
}
