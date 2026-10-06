using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Liabilities.UpdateLiability;

public record UpdateLiabilityCommand(
    Guid Id,
    string Title,
    string? LenderName = null,
    DateOnly? DueDate = null,
    string? Status = null,
    string? Notes = null) : IRequest<Unit>, IRequiresPermission
{
    public string RequiredPermission => "liabilities:manage";
}
