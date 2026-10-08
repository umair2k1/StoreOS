using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Transfers.GetPendingTransfers;

public class GetPendingTransfersHandler : IRequestHandler<GetPendingTransfersQuery, IReadOnlyList<InventoryTransferDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;

    public GetPendingTransfersHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
    }

    public async Task<IReadOnlyList<InventoryTransferDto>> Handle(GetPendingTransfersQuery request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId;
        if (!branchId.HasValue)
        {
            return Array.Empty<InventoryTransferDto>();
        }

        var transfers = await _db.Set<InventoryTransfer>()
            .Where(t => t.TenantId == _scope.TenantId
                     && t.ToBranchId == branchId.Value
                     && t.Status == TransferStatus.Pending)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        if (transfers.Count == 0)
        {
            return Array.Empty<InventoryTransferDto>();
        }

        var productIds = transfers.Select(t => t.ProductId).Distinct().ToList();
        var productNames = await _db.Set<Product>()
            .Where(p => p.TenantId == _scope.TenantId && productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        return transfers.Select(t => new InventoryTransferDto(
            t.Id,
            t.FromBranchId,
            t.ToBranchId,
            t.ProductId,
            productNames.TryGetValue(t.ProductId, out var name) ? name : null,
            t.Qty,
            t.Status,
            null,
            t.CreatedAt
        )).ToList();
    }
}
