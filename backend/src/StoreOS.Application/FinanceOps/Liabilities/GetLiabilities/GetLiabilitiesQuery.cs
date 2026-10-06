using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Liabilities.GetLiabilities;

public record GetLiabilitiesQuery(
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<LiabilityDto>>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "liabilities:manage";
}
