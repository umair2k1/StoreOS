using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Liabilities.CreateLiability;

public record CreateLiabilityCommand(
    string Title,
    string Type,
    string? LenderName,
    decimal TotalPrincipal,
    DateOnly? DueDate,
    string? Notes) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "liabilities:manage";
}
