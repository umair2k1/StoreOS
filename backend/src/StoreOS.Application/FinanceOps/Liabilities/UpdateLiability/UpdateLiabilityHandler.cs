using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Liabilities.UpdateLiability;

public class UpdateLiabilityHandler : IRequestHandler<UpdateLiabilityCommand, Unit>
{
    private readonly IAppDbContext _db;

    public UpdateLiabilityHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<Unit> Handle(UpdateLiabilityCommand request, CancellationToken ct)
    {
        var liability = await _db.Set<Liability>().FirstOrDefaultAsync(l => l.Id == request.Id, ct);
        if (liability == null)
        {
            throw new DomainException("liability_not_found", $"Liability '{request.Id}' was not found.");
        }

        liability.UpdateDescriptiveData(
            request.Title,
            request.LenderName,
            request.DueDate,
            request.Status,
            request.Notes);

        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
