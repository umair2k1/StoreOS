using Microsoft.EntityFrameworkCore;
using StoreOS.Application.FinanceOps;
using StoreOS.Domain.FinanceOps;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.FinanceOps;

public class LiabilityBalanceService : ILiabilityBalanceService
{
    private readonly StoreOsDbContext _db;

    public LiabilityBalanceService(StoreOsDbContext db)
    {
        _db = db;
    }

    public async Task<decimal> GetCurrentBalanceAsync(Guid liabilityId, CancellationToken ct)
    {
        var principal = await _db.Set<Liability>()
            .Where(l => l.Id == liabilityId)
            .Select(l => (decimal?)l.TotalPrincipal)
            .FirstOrDefaultAsync(ct);

        if (principal is null) return 0m;

        var totalPayments = await _db.Set<LiabilityPayment>()
            .Where(p => p.LiabilityId == liabilityId)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

        return Math.Max(0m, principal.Value - totalPayments);
    }
}
