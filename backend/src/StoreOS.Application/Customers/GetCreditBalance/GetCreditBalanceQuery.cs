using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.GetCreditBalance;

public record GetCreditBalanceQuery(Guid CustomerId) : IRequest<CreditBalanceDto>, IReadOnlyRequest;

public record CreditBalanceDto(
    Guid CustomerId,
    decimal ConsolidatedBalance,
    decimal? CreditLimit,
    decimal? AvailableCredit,
    bool IsOverLimit);
