using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Customers.GetCustomerLedger;

public class GetCustomerLedgerHandler : IRequestHandler<GetCustomerLedgerQuery, PagedResult<CustomerLedgerEntryDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetCustomerLedgerHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<PagedResult<CustomerLedgerEntryDto>> Handle(GetCustomerLedgerQuery request, CancellationToken ct)
    {
        var customerExists = await _db.Set<Customer>().IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == _scope.TenantId && c.Id == request.CustomerId, ct);

        if (!customerExists)
        {
            throw new DomainValidationException("customer_not_found", $"Customer '{request.CustomerId}' was not found.");
        }

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        // Branch-scoped detail query per Decision 4 / P-506 two-query pattern.
        // Uses standard query filters (IAccessScope branch filtering).
        var baseQuery = _db.Set<CustomerLedgerEntry>()
            .AsNoTracking()
            .Where(e => e.CustomerId == request.CustomerId);

        var totalCount = await baseQuery.CountAsync(ct);

        var query = from e in baseQuery
                    join b in _db.Set<Branch>().AsNoTracking() on e.BranchId equals b.Id into branchJoin
                    from b in branchJoin.DefaultIfEmpty()
                    orderby e.EntryDate descending, e.Id descending
                    select new CustomerLedgerEntryDto(
                        e.Id,
                        e.EntryType,
                        e.Debit,
                        e.Credit,
                        e.BranchId,
                        b != null ? b.Name : null,
                        e.EntryDate,
                        e.RefId);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<CustomerLedgerEntryDto>(items, totalCount, page, pageSize);
    }
}
