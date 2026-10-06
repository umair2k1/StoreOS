using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Liabilities.RecordRepayment;

public class RecordRepaymentHandler : IRequestHandler<RecordRepaymentCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly ILiabilityBalanceService _balanceService;

    public RecordRepaymentHandler(
        IAppDbContext db,
        IAccessScope scope,
        ILiabilityBalanceService balanceService)
    {
        _db = db;
        _scope = scope;
        _balanceService = balanceService;
    }

    public async Task<Guid> Handle(RecordRepaymentCommand request, CancellationToken ct)
    {
        var liability = await _db.Set<Liability>().FirstOrDefaultAsync(l => l.Id == request.LiabilityId, ct);
        if (liability == null)
        {
            throw new DomainException("liability_not_found", $"Liability '{request.LiabilityId}' was not found.");
        }

        var currentBalance = await _balanceService.GetCurrentBalanceAsync(request.LiabilityId, ct);
        if (request.Amount > currentBalance)
        {
            throw new DomainValidationException(
                "repayment_exceeds_balance",
                $"Repayment amount {request.Amount} exceeds current balance {currentBalance}.");
        }

        var payment = LiabilityPayment.Create(
            _scope.TenantId,
            request.LiabilityId,
            request.Amount,
            request.PaymentMode,
            request.PaymentDate,
            request.Notes);

        await _db.Set<LiabilityPayment>().AddAsync(payment, ct);
        await _db.SaveChangesAsync(ct);

        return payment.Id;
    }
}
