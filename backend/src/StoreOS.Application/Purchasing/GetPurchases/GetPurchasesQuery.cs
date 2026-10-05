using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Purchasing.GetPurchases;

public record GetPurchasesQuery(
    Guid? ProductId = null,
    Guid? BranchId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<PurchaseDto>>, IReadOnlyRequest;
