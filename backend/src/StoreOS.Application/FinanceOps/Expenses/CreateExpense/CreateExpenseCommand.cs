using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.FinanceOps.Expenses.CreateExpense;

public record CreateExpenseCommand(
    string Description,
    decimal Amount,
    string Category,
    DateOnly ExpenseDate,
    Guid? BranchId = null,
    string? Notes = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "expenses:manage";
}
