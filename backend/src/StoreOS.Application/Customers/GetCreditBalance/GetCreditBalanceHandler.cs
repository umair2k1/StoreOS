using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Customers.GetCreditBalance;

public class GetCreditBalanceHandler : IRequestHandler<GetCreditBalanceQuery, CreditBalanceDto>
{
    private readonly IAppDbContext _db;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAccessScope _scope;

    public GetCreditBalanceHandler(
        IAppDbContext db,
        ICustomerLedgerService ledgerService,
        IAccessScope scope)
    {
        _db = db;
        _ledgerService = ledgerService;
        _scope = scope;
    }

    public async Task<CreditBalanceDto> Handle(GetCreditBalanceQuery request, CancellationToken ct)
    {
        var customer = await _db.Set<Customer>().IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == request.CustomerId, ct)
            ?? throw new DomainValidationException("customer_not_found", "Customer not found.");

        var consolidatedBalance = await _ledgerService.GetConsolidatedBalanceAsync(request.CustomerId, ct);

        decimal? availableCredit = customer.CreditLimit is not null
            ? customer.CreditLimit.Value - consolidatedBalance
            : null;

        bool isOverLimit = customer.CreditLimit is not null && consolidatedBalance > customer.CreditLimit.Value;

        return new CreditBalanceDto(
            request.CustomerId,
            consolidatedBalance,
            customer.CreditLimit,
            availableCredit,
            isOverLimit);
    }
}
