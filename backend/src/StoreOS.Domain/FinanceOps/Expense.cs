using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.FinanceOps;

public class Expense
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Category { get; private set; } = default!;
    public DateOnly ExpenseDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Expense() { }

    public static Expense Create(
        Guid tenantId,
        Guid? branchId,
        string description,
        decimal amount,
        string category,
        DateOnly expenseDate,
        string? notes,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new DomainValidationException("expense_description_required", "Description is required.");

        if (amount <= 0)
            throw new DomainValidationException("expense_invalid_amount", "Amount must be greater than zero.");

        if (string.IsNullOrWhiteSpace(category))
            throw new DomainValidationException("expense_category_required", "Category is required.");

        if (expenseDate == default)
            throw new DomainValidationException("expense_date_required", "Expense date is required.");

        return new Expense
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            Description = description.Trim(),
            Amount = amount,
            Category = category.Trim(),
            ExpenseDate = expenseDate,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Expense Rehydrate(
        Guid id, Guid tenantId, Guid? branchId, string description,
        decimal amount, string category, DateOnly expenseDate, string? notes,
        Guid createdBy, DateTimeOffset createdAt)
    {
        return new Expense
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            Description = description,
            Amount = amount,
            Category = category,
            ExpenseDate = expenseDate,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = createdAt
        };
    }
}
