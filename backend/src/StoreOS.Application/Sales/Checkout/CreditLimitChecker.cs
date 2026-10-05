using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Platform;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Sales.Checkout;

public class CreditLimitChecker
{
    private readonly IAppDbContext _db;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAccessScope _scope;
    private readonly IAuditWriter _auditWriter;

    public CreditLimitChecker(
        IAppDbContext db,
        ICustomerLedgerService ledgerService,
        IAccessScope scope,
        IAuditWriter auditWriter)
    {
        _db = db;
        _ledgerService = ledgerService;
        _scope = scope;
        _auditWriter = auditWriter;
    }

    public async Task CheckAsync(Guid customerId, decimal newCreditAmount, CancellationToken ct)
    {
        if (newCreditAmount <= 0) return; // fully paid sale never needs a credit check

        var customer = await _db.Set<Customer>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == customerId, ct)
            ?? throw new DomainValidationException("customer_not_found", "Customer not found.");

        if (customer.CreditLimit is null) return; // unlimited credit

        var existingBalance = await _ledgerService.GetConsolidatedBalanceAsync(customerId, ct);
        var projectedBalance = existingBalance + newCreditAmount;

        if (projectedBalance > customer.CreditLimit.Value)
        {
            if (_scope.HasPermission("sales:credit:override"))
            {
                // Manager override permission bypasses the block and logs audit event
                await _auditWriter.WriteAsync(
                    branchId: null,
                    action: "sales.credit_limit_overridden",
                    entityType: "customer",
                    entityId: customerId.ToString(),
                    changes: new
                    {
                        CustomerName = customer.Name,
                        CreditLimit = customer.CreditLimit.Value,
                        ExistingBalance = existingBalance,
                        NewCreditAmount = newCreditAmount,
                        ProjectedBalance = projectedBalance
                    },
                    ct: ct);
                return;
            }

            throw new DomainValidationException("credit_limit_exceeded",
                $"This sale would bring the customer's balance to {projectedBalance:F2}, exceeding their credit limit of {customer.CreditLimit.Value:F2}.");
        }
    }
}
