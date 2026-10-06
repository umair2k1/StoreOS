using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Liabilities.GetLiabilities;

public class GetLiabilitiesHandler : IRequestHandler<GetLiabilitiesQuery, PagedResult<LiabilityDto>>
{
    private readonly IAppDbContext _db;

    public GetLiabilitiesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<LiabilityDto>> Handle(GetLiabilitiesQuery request, CancellationToken ct)
    {
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        var query = _db.Set<Liability>().AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var pageItems = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var liabilityIds = pageItems.Select(l => l.Id).ToList();

        var paymentsByLiability = await _db.Set<LiabilityPayment>().AsNoTracking()
            .Where(p => liabilityIds.Contains(p.LiabilityId))
            .GroupBy(p => p.LiabilityId)
            .Select(g => new { LiabilityId = g.Key, TotalPaid = g.Sum(p => p.Amount) })
            .ToDictionaryAsync(x => x.LiabilityId, x => x.TotalPaid, ct);

        var dtos = pageItems.Select(l =>
        {
            var paid = paymentsByLiability.TryGetValue(l.Id, out var sum) ? sum : 0m;
            var currentBalance = Math.Max(0m, l.TotalPrincipal - paid);
            return new LiabilityDto(
                l.Id,
                l.Title,
                l.Type,
                l.LenderName,
                l.TotalPrincipal,
                currentBalance,
                l.DueDate,
                l.Status,
                l.Notes);
        }).ToList();

        return new PagedResult<LiabilityDto>(dtos, totalCount, page, pageSize);
    }
}
