namespace StoreOS.Application.FinanceOps;

public interface ILiabilityBalanceService
{
    Task<decimal> GetCurrentBalanceAsync(Guid liabilityId, CancellationToken ct);
}
