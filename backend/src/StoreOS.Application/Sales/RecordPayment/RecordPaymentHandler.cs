using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.RecordPayment;

public class RecordPaymentHandler : IRequestHandler<RecordPaymentCommand, RecordPaymentResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAuditWriter _audit;

    public RecordPaymentHandler(
        IAppDbContext db,
        IAccessScope scope,
        ICustomerLedgerService ledgerService,
        IAuditWriter audit)
    {
        _db = db;
        _scope = scope;
        _ledgerService = ledgerService;
        _audit = audit;
    }

    public async Task<RecordPaymentResult> Handle(RecordPaymentCommand request, CancellationToken ct)
    {
        var sale = await _db.Set<Sale>().FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new DomainValidationException("sale_not_found", "Sale not found.");

        if (sale.Status != SaleStatus.Active)
        {
            throw new DomainValidationException("sale_not_active", "Cannot record a payment against a non-active sale.");
        }

        var balance = sale.GrandTotal - sale.AmountPaid;
        if (request.Amount > balance)
        {
            throw new DomainValidationException(
                "payment_exceeds_balance",
                $"Payment of {request.Amount:F2} exceeds remaining balance of {balance:F2}.");
        }

        var paymentDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var payment = CustomerPayment.Create(
            _scope.TenantId,
            sale.Id,
            request.Amount,
            request.PaymentMode,
            paymentDate,
            _scope.UserId);

        await _db.Set<CustomerPayment>().AddAsync(payment, ct);

        sale.ApplyPayment(request.Amount);

        // Financed sales do not touch the customer ledger (per P-515 and P-523 instructions)
        if (sale.CustomerId is not null &&
            sale.InvoiceType == InvoiceType.TaxInvoice &&
            sale.PaymentMode != PaymentMode.Finance)
        {
            await _ledgerService.RecordPaymentCreditAsync(
                sale.CustomerId.Value,
                sale.Id,
                request.Amount,
                sale.BranchId,
                paymentDate,
                ct);
        }

        await _audit.WriteAsync(
            sale.BranchId,
            "sale.payment_recorded",
            "Sale",
            sale.Id.ToString(),
            new { request.Amount, request.PaymentMode },
            ct);

        await _db.SaveChangesAsync(ct);

        return new RecordPaymentResult(payment.Id, sale.AmountPaid, sale.GrandTotal - sale.AmountPaid);
    }
}
