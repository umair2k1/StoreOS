using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.GetCustomerLedger;

public record GetCustomerLedgerQuery(
    Guid CustomerId,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<CustomerLedgerEntryDto>>, IReadOnlyRequest;

public record CustomerLedgerEntryDto(
    long Id,
    string EntryType,
    decimal Debit,
    decimal Credit,
    Guid? BranchId,
    string? BranchName,
    DateOnly EntryDate,
    string? RefId);
