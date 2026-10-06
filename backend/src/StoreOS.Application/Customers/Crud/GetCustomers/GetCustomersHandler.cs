using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Domain.Customers;

namespace StoreOS.Application.Customers.Crud.GetCustomers;

public class GetCustomersHandler : IRequestHandler<GetCustomersQuery, PagedResult<CustomerListItemDto>>
{
    private readonly IAppDbContext _db;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAccessScope _scope;

    public GetCustomersHandler(
        IAppDbContext db,
        ICustomerLedgerService ledgerService,
        IAccessScope scope)
    {
        _db = db;
        _ledgerService = ledgerService;
        _scope = scope;
    }

    public async Task<PagedResult<CustomerListItemDto>> Handle(GetCustomersQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        var query = _db.Set<Customer>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == _scope.TenantId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(c =>
                c.Name.ToLower().Contains(term) ||
                (c.Phone != null && c.Phone.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync(ct);

        var customers = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<CustomerListItemDto>(customers.Count);
        foreach (var c in customers)
        {
            var balance = await _ledgerService.GetConsolidatedBalanceAsync(c.Id, ct);
            items.Add(new CustomerListItemDto(
                c.Id,
                c.Name,
                c.Phone,
                c.TotalPurchases,
                balance));
        }

        return new PagedResult<CustomerListItemDto>(items, totalCount, page, pageSize);
    }
}
