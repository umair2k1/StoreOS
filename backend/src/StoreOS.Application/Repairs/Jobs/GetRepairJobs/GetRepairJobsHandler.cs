using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.GetRepairJobs;

public class GetRepairJobsHandler : IRequestHandler<GetRepairJobsQuery, PagedResult<RepairJobDto>>
{
    private readonly IAppDbContext _db;

    public GetRepairJobsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<RepairJobDto>> Handle(GetRepairJobsQuery request, CancellationToken ct)
    {
        var query = _db.Set<RepairJob>().AsNoTracking();

        if (string.Equals(request.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(j => j.Status != RepairStatus.Delivered && j.Status != RepairStatus.Cancelled);
        }
        else if (!string.IsNullOrWhiteSpace(request.Status) && !string.Equals(request.Status, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(j => j.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim().ToLower()}%";
            query = query.Where(j =>
                EF.Functions.Like(j.JobNumber.ToLower(), search) ||
                (j.CustomerName != null && EF.Functions.Like(j.CustomerName.ToLower(), search)) ||
                (j.CustomerPhone != null && EF.Functions.Like(j.CustomerPhone.ToLower(), search)) ||
                (j.DeviceModel != null && EF.Functions.Like(j.DeviceModel.ToLower(), search)) ||
                (j.SerialNumber != null && EF.Functions.Like(j.SerialNumber.ToLower(), search)) ||
                (j.Imei1 != null && EF.Functions.Like(j.Imei1.ToLower(), search)));
        }

        var totalCount = await query.CountAsync(ct);
        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        var items = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new RepairJobDto(
                j.Id,
                j.TenantId,
                j.BranchId,
                j.JobNumber,
                j.CustomerId,
                j.CustomerName,
                j.CustomerPhone,
                j.CustomerAddress,
                j.DeviceBrand,
                j.DeviceModel,
                j.DeviceType,
                j.SerialNumber,
                j.Imei1,
                j.Imei2,
                j.DeviceCondition,
                j.AccessoriesReceived,
                j.ProblemDescription,
                j.CustomerNotes,
                j.Diagnosis,
                j.TechnicianNotes,
                j.InternalNotes,
                j.Technician,
                j.EstimatedCost,
                j.ApprovedCost,
                j.PartsCost,
                j.LaborCharge,
                j.Discount,
                j.Tax,
                j.FinalTotal,
                j.AdvancePaid,
                j.BalanceDue,
                j.Status,
                j.ReceivedAt,
                j.ExpectedCompletion,
                j.CompletedAt,
                j.DeliveredAt,
                j.WarrantyUntil,
                j.ApprovalState,
                j.ApprovalTimestamp,
                j.CancellationReason,
                j.InvoiceNumber,
                j.SaleId,
                j.CreatedAt,
                j.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<RepairJobDto>(items, totalCount, page, pageSize);
    }
}
