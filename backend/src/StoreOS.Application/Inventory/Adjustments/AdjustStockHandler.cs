using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Adjustments;

public class AdjustStockHandler : IRequestHandler<AdjustStockCommand, AdjustStockResult>
{
    private readonly IInventoryStockService _stockService;
    private readonly IBranchContext _branchContext;
    private readonly IAuditWriter _audit;
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public AdjustStockHandler(IInventoryStockService stockService, IBranchContext branchContext,
        IAuditWriter audit, IAppDbContext db, IAccessScope scope)
    {
        _stockService = stockService;
        _branchContext = branchContext;
        _audit = audit;
        _db = db;
        _scope = scope;
    }

    public async Task<AdjustStockResult> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value;
        var refId = Guid.NewGuid().ToString();

        if (request.QtyDelta > 0)
        {
            await _stockService.IncreaseStockAsync(branchId, request.ProductId, request.QtyDelta,
                null, MovementType.Adjustment, "Adjustment", refId, ct);
        }
        else
        {
            await _stockService.ReserveAndDecrementStockAsync(branchId, request.ProductId,
                -request.QtyDelta, MovementType.Adjustment, "Adjustment", refId, ct);
        }

        await _audit.WriteAsync(branchId, "inventory.adjusted", "Product", request.ProductId.ToString(),
            new { request.QtyDelta, request.Reason }, ct);

        await _db.SaveChangesAsync(ct);

        var newQty = await _db.Set<BranchInventory>()
            .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == request.ProductId)
            .Select(bi => bi.StockQty)
            .FirstAsync(ct);

        return new AdjustStockResult(newQty);
    }
}
