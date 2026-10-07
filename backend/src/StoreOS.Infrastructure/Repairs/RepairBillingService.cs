using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Repairs;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Repairs;

public class RepairBillingService : IRepairBillingService
{
    private readonly StoreOsDbContext _db;

    public RepairBillingService(StoreOsDbContext db) => _db = db;

    public async Task LinkSaleAsync(Guid repairJobId, Guid saleId, string invoiceNumber, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>().FirstOrDefaultAsync(j => j.Id == repairJobId, ct)
            ?? throw new DomainValidationException("repair_job_not_found", "Repair job not found.");
        job.LinkToSale(saleId, invoiceNumber);
    }
}
