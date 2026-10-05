using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Application.Inventory.Transfers.TransferOut;

public class TransferOutHandler : IRequestHandler<TransferOutCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;

    public TransferOutHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext, IInventoryStockService stockService)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _stockService = stockService;
    }

    public async Task<Guid> Handle(TransferOutCommand request, CancellationToken ct)
    {
        var fromBranchId = _branchContext.CurrentBranchId!.Value;

        if (request.ToBranchId == fromBranchId)
        {
            throw new DomainValidationException("invalid_transfer_destination", "Cannot transfer to the same branch.");
        }

        var imeiIds = new List<Guid>();
        if (request.ImeiStrings is { Count: > 0 })
        {
            foreach (var imeiStr in request.ImeiStrings)
            {
                var imeiRow = await _db.Set<InventoryImei>()
                    .Where(i => i.TenantId == _scope.TenantId && i.BranchId == fromBranchId && i.Imei == imeiStr.Trim())
                    .OrderByDescending(i => i.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (imeiRow is null)
                {
                    throw new DomainValidationException("imei_not_found", $"IMEI {imeiStr} was not found at the source branch.");
                }

                if (imeiRow.Status != ImeiStatus.InStock)
                {
                    throw new DomainValidationException("imei_not_available", $"IMEI {imeiStr} is not available for transfer (status: {imeiRow.Status}).");
                }

                imeiRow.MarkInTransit();
                imeiIds.Add(imeiRow.Id);
            }
        }

        var transfer = InventoryTransfer.CreatePending(
            _scope.TenantId,
            fromBranchId,
            request.ToBranchId,
            request.ProductId,
            request.Qty,
            imeiIds.Count > 0 ? imeiIds.ToArray() : null,
            _scope.UserId);

        await _db.Set<InventoryTransfer>().AddAsync(transfer, ct);

        await _stockService.ReserveAndDecrementStockAsync(
            fromBranchId,
            request.ProductId,
            request.Qty,
            MovementType.TransferOut,
            "Transfer",
            transfer.Id.ToString(),
            ct);

        await _db.SaveChangesAsync(ct);

        return transfer.Id;
    }
}
