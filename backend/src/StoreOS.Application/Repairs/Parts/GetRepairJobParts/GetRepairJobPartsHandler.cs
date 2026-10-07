using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Parts.GetRepairJobParts;

public class GetRepairJobPartsHandler : IRequestHandler<GetRepairJobPartsQuery, IReadOnlyList<RepairJobPartDto>>
{
    private readonly IAppDbContext _db;

    public GetRepairJobPartsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RepairJobPartDto>> Handle(GetRepairJobPartsQuery request, CancellationToken ct)
    {
        var parts = await _db.Set<RepairJobPart>()
            .AsNoTracking()
            .Where(p => p.RepairJobId == request.RepairJobId)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new RepairJobPartDto(
                p.Id,
                p.RepairJobId,
                p.ProductId,
                p.PartName,
                p.Quantity,
                p.UnitCost,
                p.ChargeAmount,
                p.Status,
                p.CreatedAt))
            .ToListAsync(ct);

        return parts;
    }
}
