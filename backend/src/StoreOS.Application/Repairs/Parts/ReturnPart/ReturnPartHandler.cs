using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Parts.ReturnPart;

public class ReturnPartHandler : IRequestHandler<ReturnPartCommand>
{
    private readonly IAppDbContext _db;
    private readonly IInventoryStockService _stockService;

    public ReturnPartHandler(IAppDbContext db, IInventoryStockService stockService)
    {
        _db = db;
        _stockService = stockService;
    }

    public async Task Handle(ReturnPartCommand request, CancellationToken ct)
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

        if (part.Status == RepairPartStatus.Returned)
        {
            part.MarkReturned(); // Throws DomainValidationException("part_already_returned")
        }

        if (part.ProductId.HasValue && part.Status == RepairPartStatus.Consumed)
        {
            await _stockService.IncreaseStockAsync(
                job.BranchId,
                part.ProductId.Value,
                part.Quantity,
                null,
                MovementType.RepairPartReturned,
                "RepairJobPart",
                part.Id.ToString(),
                ct);
        }

        part.MarkReturned();
        await _db.SaveChangesAsync(ct);
    }
}
