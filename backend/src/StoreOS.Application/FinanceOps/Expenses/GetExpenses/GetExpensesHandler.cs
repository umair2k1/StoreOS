using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Expenses.GetExpenses;

public class GetExpensesHandler : IRequestHandler<GetExpensesQuery, PagedResult<ExpenseDto>>
{
    private readonly IAppDbContext _db;

    public GetExpensesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<ExpenseDto>> Handle(GetExpensesQuery request, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fromDate = request.From;
        var toDate = request.To;

        if (!fromDate.HasValue && !toDate.HasValue)
        {
            fromDate = new DateOnly(today.Year, today.Month, 1);
            toDate = today;
        }

        var query = _db.Set<Expense>().AsNoTracking();

        if (fromDate.HasValue)
        {
            query = query.Where(e => e.ExpenseDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(e => e.ExpenseDate <= toDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(e => e.Category == request.Category);
        }

        if (request.BranchId.HasValue)
        {
            query = query.Where(e => e.BranchId == request.BranchId.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        var items = await query
            .OrderByDescending(e => e.ExpenseDate)
            .ThenByDescending(e => e.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new ExpenseDto(
                e.Id,
                e.BranchId,
                e.Description,
                e.Amount,
                e.Category,
                e.ExpenseDate,
                e.Notes,
                e.CreatedBy,
                e.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<ExpenseDto>(items, totalCount, page, pageSize);
    }
}
