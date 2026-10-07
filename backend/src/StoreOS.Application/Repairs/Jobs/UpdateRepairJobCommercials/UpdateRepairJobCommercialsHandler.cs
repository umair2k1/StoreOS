using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJobCommercials;

public class UpdateRepairJobCommercialsHandler : IRequestHandler<UpdateRepairJobCommercialsCommand>
{
    private readonly IAppDbContext _db;

    public UpdateRepairJobCommercialsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateRepairJobCommercialsCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.Id, ct);

        if (job == null)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.Id}' was not found.");
        }

        job.SetCommercials(
            request.EstimatedCost,
            request.ApprovedCost,
            request.PartsCost,
            request.LaborCharge,
            request.Discount,
            request.Tax,
            request.FinalTotal,
            request.AdvancePaid);

        await _db.SaveChangesAsync(ct);
    }
}
