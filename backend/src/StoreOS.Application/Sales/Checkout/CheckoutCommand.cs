using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.Checkout;

public record CheckoutCartLine(
    Guid ProductId,
    int Qty,
    string? Imei,
    bool UseMarginScheme,
    bool IsGift,
    decimal? OverridePricePerUnit);

public record CheckoutPayment(
    string PaymentMode,
    decimal Amount);

public record CheckoutFinanceDetails(
    string FinanceProvider,
    string? FinancerName,
    decimal DownPayment,
    int TenureMonths,
    decimal AnnualInterestRate);

public record CheckoutCommand(
    IReadOnlyList<CheckoutCartLine> Lines,
    Guid? CustomerId,
    string? WalkInCustomerName,
    decimal InvoiceDiscount,
    string InvoiceType,
    IReadOnlyList<CheckoutPayment> Payments,
    CheckoutFinanceDetails? Finance,
    string? Description
) : IRequest<CheckoutResult>, IRequiresPermission, ISerializableRequest, IIdempotentRequest
{
    public string RequiredPermission => "pos:checkout";
    public string? IdempotencyKey { get; init; }
    public string IdempotencyScope => "pos.checkout";
    public bool IdempotencyRequired => true;
}

public record CheckoutResult(
    Guid SaleId,
    string InvoiceNumber,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Balance,
    DateTimeOffset SaleDate);
