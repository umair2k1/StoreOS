using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.ConvertEstimate;

public class ConvertEstimateHandler : IRequestHandler<ConvertEstimateCommand, ConvertEstimateResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly CreditLimitChecker _creditLimitChecker;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;
    private readonly IAuditWriter _audit;

    public ConvertEstimateHandler(
        IAppDbContext db,
        IAccessScope scope,
        IBranchContext branchContext,
        IInventoryStockService stockService,
        ICustomerLedgerService ledgerService,
        CreditLimitChecker creditLimitChecker,
        IInvoiceSequenceAllocator sequenceAllocator,
        IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _stockService = stockService;
        _ledgerService = ledgerService;
        _creditLimitChecker = creditLimitChecker;
        _sequenceAllocator = sequenceAllocator;
        _audit = audit;
    }

    public async Task<ConvertEstimateResult> Handle(ConvertEstimateCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId
            ?? throw new DomainValidationException("branch_context_required", "Branch context is required.");

        // Step 2: load estimate, verify type/tenant/branch access
        var estimate = await _db.Set<Sale>()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == request.EstimateId, ct)
            ?? throw new DomainValidationException("estimate_not_found", "Estimate not found.");

        if (estimate.InvoiceType != InvoiceType.Estimate)
            throw new DomainValidationException("not_an_estimate", "Sale is not an estimate.");
        if (estimate.LinkedTaxInvoiceId is not null)
            throw new DomainValidationException("estimate_already_converted", "This estimate has already been converted.");
        if (estimate.BranchId != branchId)
            throw new DomainValidationException("branch_mismatch", "Estimate belongs to a different branch.");

        // Step 3: verify current stock/IMEI availability for every physical line
        foreach (var item in estimate.Items.Where(i => StoreOS.Domain.Catalog.ProductCategory.IsPhysical(i.Category)))
        {
            if (!string.IsNullOrEmpty(item.Imei))
            {
                await _stockService.LoadImeiForSaleAsync(branchId, item.Imei, ct); // throws if unavailable / sold
            }
            else
            {
                var available = await _db.Set<StoreOS.Domain.Inventory.BranchInventory>().AsNoTracking()
                    .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == item.ProductId)
                    .Select(bi => bi.StockQty).FirstOrDefaultAsync(ct);

                if (available < item.Qty)
                {
                    throw new DomainValidationException("insufficient_stock",
                        $"Insufficient stock for '{item.ItemName}' to convert this estimate.");
                }
            }
        }

        // Step 4: credit validation if applicable
        var requestedPaid = request.Payments.Sum(p => p.Amount);
        var newCreditAmount = Math.Max(0, estimate.GrandTotal - requestedPaid);
        if (estimate.CustomerId is not null)
        {
            await _creditLimitChecker.CheckAsync(estimate.CustomerId.Value, newCreditAmount, ct);
        }

        // Step 5: reserve new tax invoice number
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var branch = await _db.Set<StoreOS.Domain.Tenancy.Branch>().AsNoTracking()
            .FirstAsync(b => b.Id == branchId, ct);
        var fyLabel = InvoiceNumberFormatter.FinancialYearLabel(businessDate);
        var seriesKey = $"FY{fyLabel}";
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
        var invoiceNumber = InvoiceNumberFormatter.FormatFyConsecutive(branch.Code, fyLabel, seq);

        // Step 6/7: create the new tax invoice sale + items (copying line data from the estimate)
        var newSale = Sale.CreateTaxInvoice(
            _scope.TenantId, branchId, invoiceNumber, DateTimeOffset.UtcNow,
            estimate.CustomerId, estimate.CustomerName, estimate.CustomerGstin,
            estimate.TotalTaxable, estimate.TotalCgst, estimate.TotalSgst,
            estimate.GrandTotal, estimate.Discount, 0m,
            null, null, null, null, null, null,
            estimate.Description, null, _scope.UserId);

        newSale.MarkConvertedFrom(estimate.Id);
        await _db.Set<Sale>().AddAsync(newSale, ct);

        foreach (var item in estimate.Items)
        {
            var newItem = SaleItem.Create(
                _scope.TenantId, newSale.Id, item.ProductId, item.ItemName, item.Category,
                item.Qty, item.PricePerUnit, item.PurchasePriceSnapshot, item.HsnSnapshot,
                item.IsMarginScheme, item.IsGift, item.Taxable, item.Cgst, item.Sgst, item.Imei);

            newSale.AddItem(newItem);
            await _db.Set<SaleItem>().AddAsync(newItem, ct);

            if (StoreOS.Domain.Catalog.ProductCategory.IsPhysical(item.Category) && !item.IsGift)
            {
                await _stockService.ReserveAndDecrementStockAsync(
                    branchId, item.ProductId!.Value, item.Qty,
                    StoreOS.Domain.Inventory.MovementType.Sale, "Sale", newSale.Id.ToString(), ct);

                if (!string.IsNullOrEmpty(item.Imei))
                {
                    var snapshot = await _stockService.LoadImeiForSaleAsync(branchId, item.Imei, ct);
                    await _stockService.SellImeiAsync(snapshot.Id, newSale.Id, newItem.Id, ct);
                }
            }
        }

        // Step 9: payments + ledger
        var (payments, totalPaid) = PaymentComposer.Compose(
            _scope.TenantId, newSale.Id, request.Payments, businessDate, _scope.UserId);

        foreach (var p in payments)
        {
            await _db.Set<CustomerPayment>().AddAsync(p, ct);
        }

        if (totalPaid > 0)
        {
            newSale.ApplyPayment(totalPaid);
        }

        if (estimate.CustomerId is not null)
        {
            await _ledgerService.RecordInvoiceDebitAsync(
                estimate.CustomerId.Value, newSale.Id, estimate.GrandTotal, branchId, businessDate, ct);

            if (totalPaid > 0)
            {
                await _ledgerService.RecordPaymentCreditAsync(
                    estimate.CustomerId.Value, newSale.Id, totalPaid, branchId, businessDate, ct);
            }

            var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == estimate.CustomerId.Value, ct);
            customer.AddToTotalPurchases(estimate.GrandTotal);
        }

        // Step 10: update the original estimate
        estimate.MarkConvertedToTaxInvoice(newSale.Id);

        // Step 11: audit
        await _audit.WriteAsync(
            branchId, "estimate.converted", "Sale", newSale.Id.ToString(),
            new { EstimateId = estimate.Id, invoiceNumber }, ct);

        await _db.SaveChangesAsync(ct);

        return new ConvertEstimateResult(
            newSale.Id, invoiceNumber, estimate.GrandTotal, totalPaid,
            Math.Max(0, estimate.GrandTotal - totalPaid));
    }
}
