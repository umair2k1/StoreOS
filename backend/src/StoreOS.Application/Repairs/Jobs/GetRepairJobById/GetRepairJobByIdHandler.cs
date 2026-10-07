using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.GetRepairJobById;

public class GetRepairJobByIdHandler : IRequestHandler<GetRepairJobByIdQuery, RepairJobDto>
{
    private readonly IAppDbContext _db;

    public GetRepairJobByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<RepairJobDto> Handle(GetRepairJobByIdQuery request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.Id, ct);

        if (job == null)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.Id}' was not found.");
        }

        return new RepairJobDto(
            job.Id,
            job.TenantId,
            job.BranchId,
            job.JobNumber,
            job.CustomerId,
            job.CustomerName,
            job.CustomerPhone,
            job.CustomerAddress,
            job.DeviceBrand,
            job.DeviceModel,
            job.DeviceType,
            job.SerialNumber,
            job.Imei1,
            job.Imei2,
            job.DeviceCondition,
            job.AccessoriesReceived,
            job.ProblemDescription,
            job.CustomerNotes,
            job.Diagnosis,
            job.TechnicianNotes,
            job.InternalNotes,
            job.Technician,
            job.EstimatedCost,
            job.ApprovedCost,
            job.PartsCost,
            job.LaborCharge,
            job.Discount,
            job.Tax,
            job.FinalTotal,
            job.AdvancePaid,
            job.BalanceDue,
            job.Status,
            job.ReceivedAt,
            job.ExpectedCompletion,
            job.CompletedAt,
            job.DeliveredAt,
            job.WarrantyUntil,
            job.ApprovalState,
            job.ApprovalTimestamp,
            job.CancellationReason,
            job.InvoiceNumber,
            job.SaleId,
            job.CreatedAt,
            job.UpdatedAt);
    }
}
