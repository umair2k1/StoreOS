using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Liabilities.GetLiabilityById;

public class GetLiabilityByIdHandler : IRequestHandler<GetLiabilityByIdQuery, LiabilityDto>
{
    private readonly IAppDbContext _db;
    private readonly ILiabilityBalanceService _balanceService;

    public GetLiabilityByIdHandler(IAppDbContext db, ILiabilityBalanceService balanceService)
    {
        _db = db;
        _balanceService = balanceService;
    }

    public async Task<LiabilityDto> Handle(GetLiabilityByIdQuery request, CancellationToken ct)
    {
        var liability = await _db.Set<Liability>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == request.Id, ct);

        if (liability == null)
        {
            throw new DomainException("liability_not_found", $"Liability '{request.Id}' was not found.");
        }

        var currentBalance = await _balanceService.GetCurrentBalanceAsync(request.Id, ct);

        return new LiabilityDto(
            liability.Id,
            liability.Title,
            liability.Type,
            liability.LenderName,
            liability.TotalPrincipal,
            currentBalance,
            liability.DueDate,
            liability.Status,
            liability.Notes);
    }
}
