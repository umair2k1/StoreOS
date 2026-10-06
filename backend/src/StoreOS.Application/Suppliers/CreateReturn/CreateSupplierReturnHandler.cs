using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Suppliers.CreateReturn;

public class CreateSupplierReturnHandler : IRequestHandler<CreateSupplierReturnCommand, CreateSupplierReturnResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;
    private readonly ISupplierLedgerService _supplierLedger;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;
    private readonly IAuditWriter _audit;

    public CreateSupplierReturnHandler(
        IAppDbContext db,
        IAccessScope scope,
        IBranchContext branchContext,
        IInventoryStockService stockService,
        ISupplierLedgerService supplierLedger,
        IInvoiceSequenceAllocator sequenceAllocator,
        IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _stockService = stockService;
        _supplierLedger = supplierLedger;
        _sequenceAllocator = sequenceAllocator;
        _audit = audit;
    }

    public async Task<CreateSupplierReturnResult> Handle(CreateSupplierReturnCommand request, CancellationToken ct)
    {
        if (!_branchContext.HasBranchContext || !_branchContext.CurrentBranchId.HasValue)
            throw new DomainException("branch_context_required", "This operation requires branch context.");

        var branchId = _branchContext.CurrentBranchId.Value;
        var branch = await _db.Set<Branch>().FirstOrDefaultAsync(b => b.Id == branchId, ct)
            ?? throw new DomainException("branch_not_found", "Branch not found.");

        var supplier = await _db.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
            ?? throw new DomainValidationException("supplier_not_found", "Supplier not found.");

        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId,
            $"RETURN-{request.ReturnDate:yyyyMMdd}", ct);
        var returnNumber = SupplierReturnNumberFormatter.Format(branch.Code, request.ReturnDate, seq);

        var supplierReturn = SupplierReturn.Create(_scope.TenantId, branchId, returnNumber,
            request.ReturnDate, request.SupplierId, supplier.Phone, supplier.Address,
            request.SettlementType, request.Reason, request.Notes, _scope.UserId);

        // Preliminary flush so supplierReturn.Id is persisted and available as refId for stock service
        await _db.Set<SupplierReturn>().AddAsync(supplierReturn, ct);
        await _db.SaveChangesAsync(ct);

        decimal totalAmount = 0m;
        int totalQty = 0;

        foreach (var line in request.Lines)
        {
            if (line.ProductId is not null)
            {
                await _stockService.ReserveAndDecrementStockAsync(branchId, line.ProductId.Value, line.Qty,
                    MovementType.SupplierReturn, "SupplierReturn", supplierReturn.Id.ToString(), ct);
            }

            var lineTotal = Money.Round2(line.Qty * line.ReturnRate);
            var item = SupplierReturnItem.Create(_scope.TenantId, supplierReturn.Id, line.ProductId,
                line.ItemName, line.Qty, line.PurchasePrice, line.ReturnRate, lineTotal, line.ImeiNumbers);
            supplierReturn.AddItem(item);
            await _db.Set<SupplierReturnItem>().AddAsync(item, ct);

            totalAmount += lineTotal;
            totalQty += line.Qty;

            if (line.ImeiNumbers is { Length: > 0 })
            {
                foreach (var imeiStr in line.ImeiNumbers)
                {
                    var imeiRow = await _db.Set<InventoryImei>()
                        .FirstOrDefaultAsync(i => i.TenantId == _scope.TenantId && i.BranchId == branchId && i.Imei == imeiStr, ct)
                        ?? throw new DomainValidationException("imei_not_found", $"IMEI {imeiStr} not found at this branch.");
                    imeiRow.MarkReturnedToSupplier(supplierReturn.Id);
                }
            }
        }

        supplierReturn.SetTotals(totalQty, totalAmount);

        // Ledger posting by settlement type (Plan §10.4):
        switch (request.SettlementType)
        {
            case SettlementType.PayableDeduction:
                await _supplierLedger.RecordReturnCreditAsync(request.SupplierId, totalAmount, branchId, request.ReturnDate, supplierReturn.Id.ToString(), ct);
                break;
            case SettlementType.CashRefund:
                // Cash refund doesn't reduce payable — it's an immediate cash transaction outside the ledger's
                // payable tracking; still worth an audit trail but NOT a ledger entry, since no payable existed
                // to reduce for a pure cash-in-hand refund scenario. Documented design decision.
                break;
            case SettlementType.PendingCredit:
                // Recorded but not yet applied — still worth a ledger credit marking the credit as
                // available, using the SAME ReturnCredit entry type; a later manual application
                // against a future bill is a separate transaction (Payment/Bill), not modeled specially here.
                await _supplierLedger.RecordReturnCreditAsync(request.SupplierId, totalAmount, branchId, request.ReturnDate, supplierReturn.Id.ToString(), ct);
                break;
            default:
                throw new DomainValidationException("invalid_settlement_type", $"Unknown settlement type '{request.SettlementType}'.");
        }

        await _audit.WriteAsync(branchId, "supplier_return.created", "SupplierReturn", supplierReturn.Id.ToString(),
            new { returnNumber, totalAmount, request.SettlementType }, ct);

        await _db.SaveChangesAsync(ct);

        return new CreateSupplierReturnResult(supplierReturn.Id, returnNumber, totalAmount);
    }
}
