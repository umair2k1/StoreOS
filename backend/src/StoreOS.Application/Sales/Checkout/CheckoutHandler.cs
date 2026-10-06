using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tax;

namespace StoreOS.Application.Sales.Checkout;

public class CheckoutHandler : IRequestHandler<CheckoutCommand, CheckoutResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly PricingResolver _pricingResolver;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly CreditLimitChecker _creditLimitChecker;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;
    private readonly IAuditWriter _audit;

    public CheckoutHandler(
        IAppDbContext db,
        IAccessScope scope,
        IBranchContext branchContext,
        PricingResolver pricingResolver,
        IInventoryStockService stockService,
        ICustomerLedgerService ledgerService,
        CreditLimitChecker creditLimitChecker,
        IInvoiceSequenceAllocator sequenceAllocator,
        IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _branchContext = branchContext;
        _pricingResolver = pricingResolver;
        _stockService = stockService;
        _ledgerService = ledgerService;
        _creditLimitChecker = creditLimitChecker;
        _sequenceAllocator = sequenceAllocator;
        _audit = audit;
    }

    public async Task<CheckoutResult> Handle(CheckoutCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId
            ?? throw new DomainValidationException("branch_context_required", "Branch context is required for checkout.");
        var saleDate = DateTimeOffset.UtcNow;
        var businessDate = DateOnly.FromDateTime(saleDate.UtcDateTime);

        // Step 2: Resolve pricing for every line (server-side, ignoring client totals)
        var resolvedLines = new List<ResolvedCartLine>();
        foreach (var line in request.Lines)
        {
            resolvedLines.Add(await _pricingResolver.ResolveAsync(branchId, line, ct));
        }

        var subtotal = resolvedLines.Sum(l => l.PricePerUnit * l.Qty);
        var taxableBeforeDiscount = resolvedLines.Sum(l => l.LineTaxable);
        var cgstBeforeDiscount = resolvedLines.Sum(l => l.LineCgst);
        var sgstBeforeDiscount = resolvedLines.Sum(l => l.LineSgst);

        // Discount application
        decimal totalTaxable = taxableBeforeDiscount;
        decimal totalCgst = cgstBeforeDiscount;
        decimal totalSgst = sgstBeforeDiscount;
        if (request.InvoiceDiscount > 0)
        {
            var beforeAgg = new LineTax(taxableBeforeDiscount, cgstBeforeDiscount, sgstBeforeDiscount);
            var afterAgg = DiscountEngine.ApplyProportionalDiscount(beforeAgg, subtotal, request.InvoiceDiscount);
            totalTaxable = afterAgg.Taxable;
            totalCgst = afterAgg.Cgst;
            totalSgst = afterAgg.Sgst;
        }
        var grandTotal = StoreOS.Domain.Common.Money.Round2(subtotal - request.InvoiceDiscount);

        // Step 3: Credit validation
        Guid? customerId = request.CustomerId;
        var requestedPaid = request.Payments.Sum(p => p.Amount);
        var newCreditAmount = Math.Max(0, grandTotal - requestedPaid);
        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
        {
            await _creditLimitChecker.CheckAsync(customerId.Value, newCreditAmount, ct);
        }

        var branch = await _db.Set<StoreOS.Domain.Tenancy.Branch>().AsNoTracking().FirstAsync(b => b.Id == branchId, ct);

        // Step 1: Create the sale header
        var customerName = request.WalkInCustomerName ?? "Walk-in Customer";
        string? customerGstin = null;
        if (customerId is not null)
        {
            var customerEntity = await _db.Set<Customer>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId.Value, ct);
            if (customerEntity != null)
            {
                customerName = customerEntity.Name;
                customerGstin = customerEntity.Gstin;
            }
        }

        var placeholderInvoice = Guid.NewGuid().ToString("N");
        var sale = request.InvoiceType == InvoiceType.TaxInvoice
            ? Sale.CreateTaxInvoice(
                _scope.TenantId, branchId, placeholderInvoice, saleDate, customerId, customerName, customerGstin,
                totalTaxable, totalCgst, totalSgst, grandTotal, request.InvoiceDiscount, 0m,
                null, null, null, null, null, null, request.Description, null, _scope.UserId)
            : Sale.CreateEstimate(
                _scope.TenantId, branchId, placeholderInvoice, saleDate, customerId, customerName, customerGstin,
                totalTaxable, totalCgst, totalSgst, grandTotal, request.InvoiceDiscount, 0m,
                null, null, null, null, null, null, request.Description, null, _scope.UserId);

        await _db.Set<Sale>().AddAsync(sale, ct);
        // Corrected ordering (fixes Guid.Empty issue):
        // Create SaleItem entities FIRST so we have their real IDs client-side before stock/IMEI reservation
        var itemsWithLines = new List<(SaleItem Item, ResolvedCartLine Line)>();
        foreach (var line in resolvedLines)
        {
            var item = SaleItem.Create(
                _scope.TenantId, sale.Id, line.ProductId, line.ProductName,
                line.Category, line.Qty, line.PricePerUnit, line.PurchasePriceSnapshot, line.HsnCode,
                line.IsMarginScheme, line.IsGift, line.LineTaxable, line.LineCgst, line.LineSgst, line.Imei);

            sale.AddItem(item);
            await _db.Set<SaleItem>().AddAsync(item, ct);
            itemsWithLines.Add((item, line));
        }

        // Step 5: Stock & IMEI reservation (skipped entirely for Estimate per P-517)
        if (request.InvoiceType == InvoiceType.TaxInvoice)
        {
            foreach (var (item, line) in itemsWithLines)
            {
                if (!StoreOS.Domain.Catalog.ProductCategory.IsPhysical(line.Category)) continue;

                // Decrement stock for physical items
                await _stockService.ReserveAndDecrementStockAsync(
                    branchId, line.ProductId, line.Qty,
                    StoreOS.Domain.Inventory.MovementType.Sale, "Sale", sale.Id.ToString(), ct);

                // If serialized, thread real item.Id into SellImeiAsync instead of Guid.Empty
                if (line.ImeiRowId is not null)
                {
                    await _stockService.SellImeiAsync(line.ImeiRowId.Value, sale.Id, item.Id, ct);
                }
            }
        }

        // Step 8: Payments & ledger
        var (payments, totalPaid) = PaymentComposer.Compose(
            _scope.TenantId, sale.Id, request.Payments, businessDate, _scope.UserId);

        foreach (var payment in payments)
        {
            await _db.Set<CustomerPayment>().AddAsync(payment, ct);
        }

        if (totalPaid > 0)
        {
            sale.ApplyPayment(totalPaid);
        }

        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
        {
            await _ledgerService.RecordInvoiceDebitAsync(customerId.Value, sale.Id, grandTotal, branchId, businessDate, ct);
            if (totalPaid > 0)
            {
                await _ledgerService.RecordPaymentCreditAsync(customerId.Value, sale.Id, totalPaid, branchId, businessDate, ct);
            }

            var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == customerId.Value, ct);
            customer.AddToTotalPurchases(grandTotal);
        }

        // Step 9: Reserve invoice sequence immediately before commit
        var seriesKey = $"FY{InvoiceNumberFormatter.FinancialYearLabel(businessDate)}";
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
        var fyLabel = InvoiceNumberFormatter.FinancialYearLabel(businessDate);
        var invoiceNumber = request.InvoiceType == InvoiceType.TaxInvoice
            ? InvoiceNumberFormatter.FormatFyConsecutive(branch.Code, fyLabel, seq)
            : InvoiceNumberFormatter.FormatEstimate(branch.Code, fyLabel, seq);
        typeof(Sale).GetProperty(nameof(Sale.InvoiceNumber))!.SetValue(sale, invoiceNumber);

        // Step 10: Audit log
        await _audit.WriteAsync(
            branchId,
            "sale.checkout",
            "Sale",
            sale.Id.ToString(),
            new { invoiceNumber, grandTotal, totalPaid, request.InvoiceType },
            ct);

        await _db.SaveChangesAsync(ct);

        return new CheckoutResult(
            sale.Id,
            invoiceNumber,
            grandTotal,
            totalPaid,
            Math.Max(0, grandTotal - totalPaid),
            saleDate);
    }
}
