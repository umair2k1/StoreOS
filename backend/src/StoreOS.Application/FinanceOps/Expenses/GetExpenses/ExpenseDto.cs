namespace StoreOS.Application.FinanceOps.Expenses.GetExpenses;

public record ExpenseDto(
    Guid Id,
    Guid? BranchId,
    string Description,
    decimal Amount,
    string Category,
    DateOnly ExpenseDate,
    string? Notes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt);
