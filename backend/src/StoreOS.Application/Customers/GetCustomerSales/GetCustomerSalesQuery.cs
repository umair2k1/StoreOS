using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Search;

namespace StoreOS.Application.Customers.GetCustomerSales;

public record GetCustomerSalesQuery(
    Guid CustomerId,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<SaleSummaryDto>>, IReadOnlyRequest;
