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

        var chargeableLines = resolvedLines.Where(l => !l.IsGift).ToList();
        var subtotal = chargeableLines.Sum(l => l.PricePerUnit * l.Qty);
        var taxableBeforeDiscount = chargeableLines.Sum(l => l.LineTaxable);
        var cgstBeforeDiscount = chargeableLines.Sum(l => l.LineCgst);
        var sgstBeforeDiscount = chargeableLines.Sum(l => l.LineSgst);
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
        if (grandTotal == 0 && requestedPaid > 0)
        {
            throw new DomainValidationException("payment_exceeds_balance", "Payment amount cannot exceed grand total of 0.");
        }
        var newCreditAmount = Math.Max(0, grandTotal - requestedPaid);
        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice && request.Finance is null)
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
        var isTaxInvoice = string.IsNullOrWhiteSpace(request.InvoiceType) || request.InvoiceType.Equals(InvoiceType.TaxInvoice, StringComparison.OrdinalIgnoreCase);
        var sale = isTaxInvoice
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
        var effectivePayments = request.Payments.ToList();
        if (request.Finance is not null)
        {
            var composed = FinanceComposer.Compose(request.Finance, grandTotal);
            var emiJson = System.Text.Json.JsonSerializer.Serialize(composed);
            sale.RecordFinanceDetails(composed.FinanceProvider, composed.FinancerName, composed.DownPayment, emiJson);

            // If down payment > 0 and not already in Payments list, apply it as a payment
            if (composed.DownPayment > 0 && !effectivePayments.Any(p => p.Amount == composed.DownPayment))
            {
                effectivePayments.Add(new CheckoutPayment(PaymentMode.Finance, composed.DownPayment));
            }
        }

        var (payments, totalPaid) = PaymentComposer.Compose(
            _scope.TenantId, sale.Id, effectivePayments, businessDate, _scope.UserId);

        foreach (var payment in payments)
        {
            await _db.Set<CustomerPayment>().AddAsync(payment, ct);
        }

        if (totalPaid > 0)
        {
            sale.ApplyPayment(totalPaid);
        }

        // Ledger entries: strictly skipped when Finance is present per Hard Constraints (neither debit nor credit)
        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
        {
            if (request.Finance is null)
            {
                await _ledgerService.RecordInvoiceDebitAsync(customerId.Value, sale.Id, grandTotal, branchId, businessDate, ct);
                if (totalPaid > 0)
                {
                    await _ledgerService.RecordPaymentCreditAsync(customerId.Value, sale.Id, totalPaid, branchId, businessDate, ct);
                }
            }

            var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == customerId.Value, ct);
            customer.AddToTotalPurchases(grandTotal);
        }

        // Step 9: Reserve invoice sequence immediately before commit
        // DECISIONS D-07: Estimate uses separate series_key ("EST-FYxx-xx") so tax invoice consecutive series is never burned/gapped
        var fyLabel = InvoiceNumberFormatter.FinancialYearLabel(businessDate);
        var seriesKey = request.InvoiceType == InvoiceType.TaxInvoice
            ? $"FY{fyLabel}"
            : $"EST-FY{fyLabel}";
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
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
