using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Expenses.GetMonthlyTotal;

public class GetMonthlyTotalHandler : IRequestHandler<GetMonthlyTotalQuery, MonthlyExpenseTotalDto>
{
    private readonly IAppDbContext _db;

    public GetMonthlyTotalHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<MonthlyExpenseTotalDto> Handle(GetMonthlyTotalQuery request, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var year = request.Year ?? today.Year;
        var month = request.Month ?? today.Month;

        var firstDay = new DateOnly(year, month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);

        var query = _db.Set<Expense>().AsNoTracking()
            .Where(e => e.ExpenseDate >= firstDay && e.ExpenseDate <= lastDay);

        if (request.BranchId.HasValue)
        {
            query = query.Where(e => e.BranchId == request.BranchId.Value);
        }

        var total = await query.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        return new MonthlyExpenseTotalDto(year, month, total);
    }
}
