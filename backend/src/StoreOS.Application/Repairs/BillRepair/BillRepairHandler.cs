using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Repairs.BillRepair;

public class BillRepairHandler : IRequestHandler<BillRepairCommand, BillRepairResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IRepairBillingService _repairBillingService;
    private readonly IAuditWriter _audit;

    public BillRepairHandler(
        IAppDbContext db,
        IAccessScope scope,
        IInvoiceSequenceAllocator sequenceAllocator,
        ICustomerLedgerService ledgerService,
        IRepairBillingService repairBillingService,
        IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _sequenceAllocator = sequenceAllocator;
        _ledgerService = ledgerService;
        _repairBillingService = repairBillingService;
        _audit = audit;
    }

    public async Task<BillRepairResult> Handle(BillRepairCommand request, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>()
            .FirstOrDefaultAsync(j => j.Id == request.RepairJobId, ct)
            ?? throw new DomainValidationException("repair_job_not_found", "Repair job not found.");

        if (job.FinalTotal is null)
        {
            throw new DomainValidationException("repair_commercials_not_set", "Repair commercials (final total) must be set before billing.");
        }

        if (job.SaleId != null)
        {
            throw new DomainValidationException("repair_job_already_billed", "Repair job has already been billed.");
        }

        var branch = await _db.Set<Branch>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == job.BranchId, ct)
            ?? throw new DomainValidationException("branch_not_found", "Branch not found.");

        var grandTotal = job.FinalTotal.Value;
        var tax = job.Tax ?? 0m;
        var totalTaxable = StoreOS.Domain.Common.Money.Round2(grandTotal - tax);
        var totalCgst = StoreOS.Domain.Common.Money.Round2(tax / 2m);
        var totalSgst = tax - totalCgst;
        var discount = job.Discount ?? 0m;

        var customerName = job.CustomerName ?? "Walk-in Customer";
        string? customerGstin = null;
        if (job.CustomerId is not null)
        {
            var customerEntity = await _db.Set<Customer>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == job.CustomerId.Value, ct);
            if (customerEntity != null)
            {
                customerName = customerEntity.Name;
                customerGstin = customerEntity.Gstin;
            }
        }

        var saleDate = DateTimeOffset.UtcNow;
        var businessDate = DateOnly.FromDateTime(saleDate.UtcDateTime);
        var placeholderInvoice = Guid.NewGuid().ToString("N");

        var sale = Sale.CreateTaxInvoice(
            _scope.TenantId,
            job.BranchId,
            placeholderInvoice,
            saleDate,
            job.CustomerId,
            customerName,
            customerGstin,
            totalTaxable,
            totalCgst,
            totalSgst,
            grandTotal,
            discount,
            amountPaid: 0m,
            paymentMode: null,
            financeProvider: null,
            financerName: null,
            downPayment: null,
            emiDetailsJson: null,
            giftDetailsJson: null,
            description: $"Repair billing for job {job.JobNumber}",
            repairJobId: job.Id,
            createdBy: _scope.UserId);

        var saleItem = SaleItem.Create(
            _scope.TenantId,
            sale.Id,
            productId: null,
            itemName: $"Repair Service - {job.JobNumber}",
            category: ProductCategory.RepairService,
            qty: 1,
            pricePerUnit: grandTotal,
            purchasePriceSnapshot: job.PartsCost ?? 0m,
            hsnSnapshot: "998713",
            isMarginScheme: false,
            isGift: false,
            taxable: totalTaxable,
            cgst: totalCgst,
            sgst: totalSgst,
            imei: job.Imei1);

        sale.AddItem(saleItem);
        await _db.Set<Sale>().AddAsync(sale, ct);
        await _db.Set<SaleItem>().AddAsync(saleItem, ct);

        // Payments & Ledger
        var requestedPayments = request.Payments ?? Array.Empty<CheckoutPayment>();
        var (payments, totalPaid) = PaymentComposer.Compose(
            _scope.TenantId, sale.Id, requestedPayments, businessDate, _scope.UserId);

        foreach (var payment in payments)
        {
            await _db.Set<CustomerPayment>().AddAsync(payment, ct);
        }

        if (totalPaid > 0)
        {
            sale.ApplyPayment(totalPaid);
        }

        if (job.CustomerId is not null)
        {
            await _ledgerService.RecordInvoiceDebitAsync(job.CustomerId.Value, sale.Id, grandTotal, job.BranchId, businessDate, ct);
            if (totalPaid > 0)
            {
                await _ledgerService.RecordPaymentCreditAsync(job.CustomerId.Value, sale.Id, totalPaid, job.BranchId, businessDate, ct);
            }

            var customer = await _db.Set<Customer>().FirstOrDefaultAsync(c => c.Id == job.CustomerId.Value, ct);
            customer?.AddToTotalPurchases(grandTotal);
        }

        // Sequence Allocation
        var fyLabel = InvoiceNumberFormatter.FinancialYearLabel(businessDate);
        var seriesKey = $"FY{fyLabel}";
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, job.BranchId, seriesKey, ct);
        var invoiceNumber = InvoiceNumberFormatter.FormatFyConsecutive(branch.Code, fyLabel, seq);
        typeof(Sale).GetProperty(nameof(Sale.InvoiceNumber))!.SetValue(sale, invoiceNumber);

        // Two-step resolution linking repair job to sale
        await _repairBillingService.LinkSaleAsync(job.Id, sale.Id, invoiceNumber, ct);

        await _audit.WriteAsync(
            job.BranchId,
            "repair.billed",
            "RepairJob",
            job.Id.ToString(),
            new { saleId = sale.Id, invoiceNumber, grandTotal, totalPaid },
            ct);

        await _db.SaveChangesAsync(ct);

        return new BillRepairResult(
            sale.Id,
            invoiceNumber,
            grandTotal,
            totalPaid,
            Math.Max(0, grandTotal - totalPaid));
    }
}
