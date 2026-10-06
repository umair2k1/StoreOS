using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Customers.Anonymize;

public class AnonymizeCustomerHandler : IRequestHandler<AnonymizeCustomerCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IAuditWriter _audit;

    public AnonymizeCustomerHandler(IAppDbContext db, IAccessScope scope, IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _audit = audit;
    }

    public async Task Handle(AnonymizeCustomerCommand request, CancellationToken ct)
    {
        var customer = await _db.Set<Customer>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == request.CustomerId, ct)
            ?? throw new DomainValidationException("customer_not_found", "Customer not found.");

        customer.Anonymize();

        // Customer sales across all branches in this tenant need customer_name scrubbed
        var sales = await _db.Set<Sale>().IgnoreQueryFilters()
            .Where(s => s.TenantId == _scope.TenantId && s.CustomerId == request.CustomerId)
            .ToListAsync(ct);

        foreach (var sale in sales)
        {
            sale.ScrubCustomerNameSnapshot("Anonymized Customer");
        }

        await _audit.WriteAsync(
            branchId: null,
            action: "customer.anonymized",
            entityType: "Customer",
            entityId: customer.Id.ToString(),
            changes: null,
            ct: ct);

        await _db.SaveChangesAsync(ct);
    }
}
