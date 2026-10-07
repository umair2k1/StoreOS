using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Parts.ConsumePart;

public class ConsumePartHandler : IRequestHandler<ConsumePartCommand>
{
    private readonly IAppDbContext _db;
    private readonly IInventoryStockService _stockService;

    public ConsumePartHandler(IAppDbContext db, IInventoryStockService stockService)
    {
        _db = db;
        _stockService = stockService;
    }

    public async Task Handle(ConsumePartCommand request, CancellationToken ct)
    {
        var part = await _db.Set<RepairJobPart>()
            .FirstOrDefaultAsync(p => p.Id == request.PartId && p.RepairJobId == request.RepairJobId, ct);

        if (part == null)
        {
            throw new DomainException("repair_job_part_not_found", $"Repair job part '{request.PartId}' was not found.");
        }

        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.RepairJobId, ct);

        if (job == null)
        {
            throw new DomainException("repair_job_not_found", $"Repair job '{request.RepairJobId}' was not found.");
        }

        // Validate part can be consumed before touching stock
        if (part.Status != RepairPartStatus.Reserved)
        {
            part.MarkConsumed(); // Throws DomainValidationException("part_not_reserved")
        }

        if (part.ProductId.HasValue)
        {
            await _stockService.ReserveAndDecrementStockAsync(
                job.BranchId,
                part.ProductId.Value,
                part.Quantity,
                MovementType.RepairConsumed,
                "RepairJobPart",
                part.Id.ToString(),
                ct);
        }

        part.MarkConsumed();
        await _db.SaveChangesAsync(ct);
    }
}
