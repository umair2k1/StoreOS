using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Crud.GetCustomers;

public record GetCustomersQuery(
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<CustomerListItemDto>>, IReadOnlyRequest;

public record CustomerListItemDto(
    Guid Id,
    string Name,
    string? Phone,
    decimal TotalPurchases,
    decimal CurrentCreditBalance);
