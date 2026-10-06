using FluentValidation;

namespace StoreOS.Application.FinanceOps.Expenses.CreateExpense;

public class CreateExpenseValidator : AbstractValidator<CreateExpenseCommand>
{
    public CreateExpenseValidator()
    {
        RuleFor(x => x.Description)
            .NotEmpty().WithErrorCode("expense_description_required").WithMessage("expense_description_required");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithErrorCode("expense_invalid_amount").WithMessage("expense_invalid_amount");

        RuleFor(x => x.Category)
            .NotEmpty().WithErrorCode("expense_category_required").WithMessage("expense_category_required");

        RuleFor(x => x.ExpenseDate)
            .Must(d => d != default).WithErrorCode("expense_date_required").WithMessage("expense_date_required");
    }
}
