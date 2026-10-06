using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.GetSupplierTransactions;

public record GetSupplierTransactionsQuery(
    Guid SupplierId,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? Type = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<SupplierTransactionDto>>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
