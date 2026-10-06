using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Liabilities.GetLiabilityById;

public record GetLiabilityByIdQuery(Guid Id) : IRequest<LiabilityDto>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "liabilities:manage";
}
