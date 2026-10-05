using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;

namespace StoreOS.Application.Purchasing.StockIn;

public class StockInHandler : IRequestHandler<StockInCommand, StockInResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;
    private readonly IAuditWriter _audit;

    public StockInHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext,
        IInventoryStockService stockService, IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _stockService = stockService;
        _audit = audit;
    }

    public async Task<StockInResult> Handle(StockInCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value;

        var purchase = Purchase.Create(_scope.TenantId, branchId, request.ProductId, request.Qty,
            request.PurchaseRate, request.SupplierId, request.SupplierName, request.SupplierPhone,
            request.SupplierAddress, request.PurchaseDate, _scope.UserId, request.SupplierInvoiceNo);
        await _db.Set<Purchase>().AddAsync(purchase, ct);
        await _db.SaveChangesAsync(ct);

        await _stockService.IncreaseStockAsync(branchId, request.ProductId, request.Qty,
            request.PurchaseRate, MovementType.Purchase, "Purchase", purchase.Id.ToString(), ct);

        var createdImeiIds = new List<Guid>();
        if (request.Imeis is { Count: > 0 })
        {
            foreach (var imeiStr in request.Imeis)
            {
                var imeiRow = InventoryImei.Create(_scope.TenantId, branchId, request.ProductId,
                    purchase.Id, imeiStr, request.PurchaseDate, request.PurchaseRate);
                await _db.Set<InventoryImei>().AddAsync(imeiRow, ct);
                createdImeiIds.Add(imeiRow.Id);
            }
        }

        await _audit.WriteAsync(branchId, "purchase.stock_in", "Purchase", purchase.Id.ToString(),
            new { request.ProductId, request.Qty, request.PurchaseRate, ImeiCount = request.Imeis?.Count ?? 0 }, ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsImeiUniqueViolation(ex))
        {
            throw new DomainException("imei_already_active",
                "One or more IMEIs submitted in this purchase are already active in inventory.");
        }

        var newQty = await _db.Set<BranchInventory>()
            .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == request.ProductId)
            .Select(bi => bi.StockQty)
            .FirstAsync(ct);

        return new StockInResult(purchase.Id, newQty, createdImeiIds);
    }

    private static bool IsImeiUniqueViolation(DbUpdateException ex)
    {
        var current = ex.InnerException;
        while (current != null)
        {
            if (current is DbException dbEx && string.Equals(dbEx.SqlState, "23505", StringComparison.Ordinal))
            {
                return true;
            }

            if (current.Message.Contains("ux_imei_active", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("23505", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }
}
