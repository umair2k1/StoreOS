using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Expenses.CreateExpense;

public class CreateExpenseHandler : IRequestHandler<CreateExpenseCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;

    public CreateExpenseHandler(
        IAppDbContext db,
        IAccessScope scope,
        IBranchContext branchContext)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
    }

    public async Task<Guid> Handle(CreateExpenseCommand request, CancellationToken ct)
    {
        var branchId = request.BranchId ?? _branchContext.CurrentBranchId;

        var expense = Expense.Create(
            _scope.TenantId,
            branchId,
            request.Description,
            request.Amount,
            request.Category,
            request.ExpenseDate,
            request.Notes,
            _scope.UserId);

        await _db.Set<Expense>().AddAsync(expense, ct);
        await _db.SaveChangesAsync(ct);

        return expense.Id;
    }
}
