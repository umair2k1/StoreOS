using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.RefundSale;

public class RefundSaleHandler : IRequestHandler<RefundSaleCommand>
{
    private readonly IAppDbContext _db;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAuditWriter _audit;

    public RefundSaleHandler(
        IAppDbContext db,
        IInventoryStockService stockService,
        ICustomerLedgerService ledgerService,
        IAuditWriter audit)
    {
        _db = db;
        _stockService = stockService;
        _ledgerService = ledgerService;
        _audit = audit;
    }

    public async Task Handle(RefundSaleCommand request, CancellationToken ct)
    {
        var sale = await _db.Set<Sale>().Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new DomainValidationException("sale_not_found", "Sale not found.");

        if (sale.Status != SaleStatus.Active)
        {
            throw new DomainValidationException("sale_not_active", "Only an Active sale can be refunded.");
        }

        if (request.RefundAmount <= 0 || request.RefundAmount > sale.GrandTotal)
        {
            throw new DomainValidationException(
                "invalid_refund_amount",
                "Refund amount must be positive and not exceed the sale's grand total.");
        }

        var linesToRefund = request.SaleItemIds is null
            ? sale.Items.ToList()
            : sale.Items.Where(i => request.SaleItemIds.Contains(i.Id)).ToList();

        if (linesToRefund.Count == 0)
        {
            throw new DomainValidationException("no_lines_to_refund", "No matching sale lines found to refund.");
        }

        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var item in linesToRefund.Where(i => ProductCategory.IsPhysical(i.Category)))
        {
            if (!string.IsNullOrEmpty(item.Imei))
            {
                var imeiRow = await _db.Set<InventoryImei>()
                    .FirstAsync(i => i.TenantId == sale.TenantId && i.SaleId == sale.Id && i.SaleItemId == item.Id, ct);
                imeiRow.RestoreToStock();
            }

            await _stockService.IncreaseStockAsync(
                sale.BranchId,
                item.ProductId!.Value,
                item.Qty,
                null,
                MovementType.SaleRefund,
                "SaleRefund",
                sale.Id.ToString(),
                ct);
        }

        // Only transition Status to Refunded if all lines or whole sale are refunded.
        // A partial refund leaves the overall sale Active per requirements and DDL constraints.
        var isWholeSaleRefund = request.SaleItemIds is null || linesToRefund.Count == sale.Items.Count;
        if (isWholeSaleRefund)
        {
            sale.MarkRefunded();
        }

        if (sale.CustomerId is not null &&
            sale.InvoiceType == InvoiceType.TaxInvoice &&
            sale.PaymentMode != PaymentMode.Finance)
        {
            await _ledgerService.RecordRefundCreditAsync(
                sale.CustomerId.Value,
                sale.Id,
                request.RefundAmount,
                sale.BranchId,
                businessDate,
                ct);

            var customer = await _db.Set<StoreOS.Domain.Customers.Customer>()
                .FirstAsync(c => c.Id == sale.CustomerId.Value, ct);
            customer.SubtractFromTotalPurchases(request.RefundAmount);
        }

        await _audit.WriteAsync(
            sale.BranchId,
            "sale.refunded",
            "Sale",
            sale.Id.ToString(),
            new
            {
                request.Reason,
                request.RefundAmount,
                IsWholeSaleRefund = isWholeSaleRefund,
                RefundedLineIds = linesToRefund.Select(l => l.Id)
            },
            ct);

        await _db.SaveChangesAsync(ct);
    }
}
