using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Expenses.GetMonthlyTotal;

public record GetMonthlyTotalQuery(
    int? Year = null,
    int? Month = null,
    Guid? BranchId = null) : IRequest<MonthlyExpenseTotalDto>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "expenses:manage";
}
