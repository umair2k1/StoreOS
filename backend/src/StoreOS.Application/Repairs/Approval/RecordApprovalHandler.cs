using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Approval;

public class RecordApprovalHandler : IRequestHandler<RecordApprovalCommand>
{
    private readonly IAppDbContext _db;

    public RecordApprovalHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(RecordApprovalCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.RepairJobId, ct)
            ?? throw new DomainException("repair_job_not_found", $"Repair job '{request.RepairJobId}' was not found.");

        job.RecordApproval(request.ApprovalState, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
    }
}
