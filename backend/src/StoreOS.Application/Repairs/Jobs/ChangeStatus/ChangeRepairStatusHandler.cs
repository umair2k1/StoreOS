using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.ChangeStatus;

public class ChangeRepairStatusHandler : IRequestHandler<ChangeRepairStatusCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public ChangeRepairStatusHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(ChangeRepairStatusCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.RepairJobId, ct);

        if (job == null)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.RepairJobId}' was not found.");
        }

        var fromStatus = job.Status;
        job.TransitionTo(request.NewStatus);

        var history = RepairJobStatusHistory.Create(
            _scope.TenantId,
            job.Id,
            fromStatus,
            request.NewStatus,
            request.Notes,
            _scope.UserId);

        await _db.Set<RepairJobStatusHistory>().AddAsync(history, ct);
        await _db.SaveChangesAsync(ct);
    }
}
