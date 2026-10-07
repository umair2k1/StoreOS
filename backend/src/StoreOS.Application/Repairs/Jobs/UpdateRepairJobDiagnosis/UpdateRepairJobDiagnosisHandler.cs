using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJobDiagnosis;

public class UpdateRepairJobDiagnosisHandler : IRequestHandler<UpdateRepairJobDiagnosisCommand>
{
    private readonly IAppDbContext _db;

    public UpdateRepairJobDiagnosisHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateRepairJobDiagnosisCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.Id, ct);

        if (job == null)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.Id}' was not found.");
        }

        job.RecordDiagnosis(request.Diagnosis, request.TechnicianNotes, request.InternalNotes, request.Technician);
        await _db.SaveChangesAsync(ct);
    }
}
