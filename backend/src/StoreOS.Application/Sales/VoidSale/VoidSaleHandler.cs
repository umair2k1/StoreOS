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

namespace StoreOS.Application.Sales.VoidSale;

public class VoidSaleHandler : IRequestHandler<VoidSaleCommand>
{
    private readonly IAppDbContext _db;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAuditWriter _audit;

    public VoidSaleHandler(
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

    public async Task Handle(VoidSaleCommand request, CancellationToken ct)
    {
        var sale = await _db.Set<Sale>()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new DomainValidationException("sale_not_found", "Sale not found.");

        sale.MarkVoided(); // throws if not currently Active (guards double-void)

        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        // All physical lines (including gift lines) have their stock restored on void
        foreach (var item in sale.Items.Where(i => ProductCategory.IsPhysical(i.Category)))
        {
            if (!string.IsNullOrEmpty(item.Imei))
            {
                var imeiRow = await _db.Set<InventoryImei>()
                    .FirstAsync(i => i.TenantId == sale.TenantId && i.SaleId == sale.Id && i.SaleItemId == item.Id, ct);
                imeiRow.RestoreToStock();
                await _stockService.IncreaseStockAsync(
                    sale.BranchId,
                    item.ProductId!.Value,
                    item.Qty,
                    null,
                    MovementType.SaleVoid,
                    "SaleVoid",
                    sale.Id.ToString(),
                    ct);
            }
            else
            {
                await _stockService.IncreaseStockAsync(
                    sale.BranchId,
                    item.ProductId!.Value,
                    item.Qty,
                    null,
                    MovementType.SaleVoid,
                    "SaleVoid",
                    sale.Id.ToString(),
                    ct);
            }
        }

        if (sale.CustomerId is not null &&
            sale.InvoiceType == InvoiceType.TaxInvoice &&
            sale.PaymentMode != PaymentMode.Finance)
        {
            await _ledgerService.RecordVoidReversalAsync(
                sale.CustomerId.Value,
                sale.Id,
                sale.GrandTotal,
                sale.BranchId,
                businessDate,
                ct);

            var customer = await _db.Set<StoreOS.Domain.Customers.Customer>()
                .FirstAsync(c => c.Id == sale.CustomerId.Value, ct);
            customer.SubtractFromTotalPurchases(sale.GrandTotal);
        }

        await _audit.WriteAsync(
            sale.BranchId,
            "sale.voided",
            "Sale",
            sale.Id.ToString(),
            new { request.Reason, sale.InvoiceNumber, sale.GrandTotal },
            ct);

        await _db.SaveChangesAsync(ct);
    }
}
