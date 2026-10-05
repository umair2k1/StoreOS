using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Transfers.ReceiveTransfer;

public class ReceiveTransferHandler : IRequestHandler<ReceiveTransferCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;

    public ReceiveTransferHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext, IInventoryStockService stockService)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _stockService = stockService;
    }

    public async Task Handle(ReceiveTransferCommand request, CancellationToken ct)
    {
        var currentBranchId = _branchContext.CurrentBranchId!.Value;

        var transfer = await _db.Set<InventoryTransfer>()
            .FirstOrDefaultAsync(t => t.Id == request.TransferId, ct);

        if (transfer is null)
        {
            throw new DomainException("transfer_not_found", $"Transfer '{request.TransferId}' was not found.");
        }

        if (transfer.ToBranchId != currentBranchId)
        {
            throw new DomainValidationException("invalid_receiving_branch",
                "Only staff at the destination branch can receive this transfer.");
        }

        if (transfer.Status != TransferStatus.Pending)
        {
            throw new DomainValidationException("transfer_already_received",
                "This transfer has already been received.");
        }

        await _stockService.IncreaseStockAsync(
            currentBranchId,
            transfer.ProductId,
            transfer.Qty,
            null,
            MovementType.TransferIn,
            "Transfer",
            transfer.Id.ToString(),
            ct);

        if (transfer.ImeiIds is { Length: > 0 })
        {
            var targetIds = transfer.ImeiIds.ToList();
            var imeis = await _db.Set<InventoryImei>()
                .IgnoreQueryFilters()
                .Where(i => i.TenantId == _scope.TenantId && targetIds.Contains(i.Id))
                .ToListAsync(ct);

            foreach (var imei in imeis)
            {
                imei.CompleteTransferIn(currentBranchId);
                _db.Set<InventoryImei>().Update(imei);
            }
        }

        transfer.MarkReceived(_scope.UserId);

        await _db.SaveChangesAsync(ct);
    }
}
