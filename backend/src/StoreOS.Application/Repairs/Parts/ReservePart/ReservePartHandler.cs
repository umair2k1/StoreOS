using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Parts.ReservePart;

public class ReservePartHandler : IRequestHandler<ReservePartCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public ReservePartHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(ReservePartCommand request, CancellationToken ct)
    {
        var jobExists = await _db.Set<RepairJob>()
            .AnyAsync(j => j.Id == request.RepairJobId, ct);

        if (!jobExists)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.RepairJobId}' was not found.");
        }

        var part = RepairJobPart.Reserve(
            _scope.TenantId,
            request.RepairJobId,
            request.ProductId,
            request.PartName,
            request.Quantity,
            request.UnitCost,
            request.ChargeAmount);

        await _db.Set<RepairJobPart>().AddAsync(part, ct);
        await _db.SaveChangesAsync(ct);

        return part.Id;
    }
}
