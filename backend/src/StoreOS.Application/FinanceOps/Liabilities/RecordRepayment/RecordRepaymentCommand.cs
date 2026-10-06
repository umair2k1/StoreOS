using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Liabilities.RecordRepayment;

public record RecordRepaymentCommand(
    Guid LiabilityId,
    decimal Amount,
    string PaymentMode,
    DateOnly PaymentDate,
    string? Notes = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "liabilities:manage";
}
