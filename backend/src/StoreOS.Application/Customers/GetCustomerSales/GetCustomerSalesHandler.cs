using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Search;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Customers.GetCustomerSales;

public class GetCustomerSalesHandler : IRequestHandler<GetCustomerSalesQuery, PagedResult<SaleSummaryDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetCustomerSalesHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<PagedResult<SaleSummaryDto>> Handle(GetCustomerSalesQuery request, CancellationToken ct)
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

        var query = _db.Set<Sale>()
            .AsNoTracking()
            .Where(s => s.CustomerId == request.CustomerId);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(s => s.SaleDate)
            .ThenByDescending(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SaleSummaryDto(
                s.Id,
                s.InvoiceNumber,
                s.SaleDate,
                s.CustomerName ?? "",
                s.GrandTotal,
                s.AmountPaid,
                s.GrandTotal - s.AmountPaid,
                s.Status,
                s.InvoiceType,
                s.PaymentMode,
                s.PaymentMode == "Finance"))
            .ToListAsync(ct);

        return new PagedResult<SaleSummaryDto>(items, totalCount, page, pageSize);
    }
}
