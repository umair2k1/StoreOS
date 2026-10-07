using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJob;

public class UpdateRepairJobHandler : IRequestHandler<UpdateRepairJobCommand>
{
    private readonly IAppDbContext _db;

    public UpdateRepairJobHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateRepairJobCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.Id, ct);

        if (job == null)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.Id}' was not found.");
        }

        job.UpdateIntakeDetails(
            request.CustomerNotes ?? job.CustomerNotes,
            request.ProblemDescription ?? job.ProblemDescription,
            request.DeviceCondition ?? job.DeviceCondition,
            request.AccessoriesReceived ?? job.AccessoriesReceived,
            request.ExpectedCompletion ?? job.ExpectedCompletion);

        await _db.SaveChangesAsync(ct);
    }
}
