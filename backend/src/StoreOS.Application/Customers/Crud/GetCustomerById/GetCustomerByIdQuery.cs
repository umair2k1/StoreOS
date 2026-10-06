using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Crud.GetCustomerById;

public record GetCustomerByIdQuery(Guid Id) : IRequest<CustomerDetailDto>, IReadOnlyRequest;

public record CustomerDetailDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Gstin,
    decimal TotalPurchases,
    decimal? CreditLimit,
    decimal CurrentCreditBalance,
    DateTimeOffset CreatedAt);
