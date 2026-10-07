using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Deliver;

public class DeliverRepairJobHandler : IRequestHandler<DeliverRepairJobCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _accessScope;

    public DeliverRepairJobHandler(IAppDbContext db, IAccessScope accessScope)
    {
        _db = db;
        _accessScope = accessScope;
    }

    public async Task Handle(DeliverRepairJobCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.RepairJobId, ct)
            ?? throw new DomainException("repair_job_not_found", $"Repair job '{request.RepairJobId}' was not found.");

        var fromStatus = job.Status;

        int defaultWarrantyDays = 30;
        var parts = await _db.Set<RepairJobPart>()
            .Where(p => p.RepairJobId == job.Id && p.ProductId != null)
            .ToListAsync(ct);

        if (parts.Count > 0)
        {
            var productIds = parts.Select(p => p.ProductId!.Value).Distinct().ToList();
            var serviceProducts = await _db.Set<Product>()
                .Where(p => productIds.Contains(p.Id) && p.WarrantyDays != null)
                .ToListAsync(ct);

            if (serviceProducts.Count == 1 && serviceProducts[0].WarrantyDays.HasValue)
            {
                defaultWarrantyDays = serviceProducts[0].WarrantyDays!.Value;
            }
        }

        int warrantyDays = request.WarrantyDaysOverride ?? defaultWarrantyDays;
        var now = DateTimeOffset.UtcNow;
        var warrantyUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(warrantyDays);

        job.TransitionTo(RepairStatus.Delivered);
        job.Deliver(now, warrantyUntil);

        var history = RepairJobStatusHistory.Create(
            job.TenantId,
            job.Id,
            fromStatus,
            RepairStatus.Delivered,
            "Delivered to customer",
            _accessScope.UserId);
        await _db.Set<RepairJobStatusHistory>().AddAsync(history, ct);

        await _db.SaveChangesAsync(ct);
    }
}
