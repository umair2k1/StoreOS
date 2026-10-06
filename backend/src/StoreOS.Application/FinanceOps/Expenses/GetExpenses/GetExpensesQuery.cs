using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Expenses.GetExpenses;

public record GetExpensesQuery(
    DateOnly? From = null,
    DateOnly? To = null,
    string? Category = null,
    Guid? BranchId = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<ExpenseDto>>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => "expenses:manage";
}
