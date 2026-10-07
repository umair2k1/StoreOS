using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.GetStatusHistory;

public class GetRepairStatusHistoryHandler : IRequestHandler<GetRepairStatusHistoryQuery, IReadOnlyList<RepairStatusHistoryDto>>
{
    private readonly IAppDbContext _db;

    public GetRepairStatusHistoryHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<RepairStatusHistoryDto>> Handle(GetRepairStatusHistoryQuery request, CancellationToken ct)
    {
        var history = await _db.Set<RepairJobStatusHistory>()
            .AsNoTracking()
            .Where(h => h.RepairJobId == request.RepairJobId)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .Select(h => new RepairStatusHistoryDto(
                h.Id,
                h.RepairJobId,
                h.FromStatus,
                h.ToStatus,
                h.Notes,
                h.ChangedBy,
                h.ChangedAt))
            .ToListAsync(ct);

        return history;
    }
}
