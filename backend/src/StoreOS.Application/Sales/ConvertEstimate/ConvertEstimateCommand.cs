using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.ConvertEstimate;

public record ConvertEstimateCommand(
    Guid EstimateId,
    IReadOnlyList<Checkout.CheckoutPayment> Payments
) : IRequest<ConvertEstimateResult>, IRequiresPermission, ISerializableRequest, IIdempotentRequest
{
    public string RequiredPermission => "pos:checkout";
    public string? IdempotencyKey { get; init; }
    public string IdempotencyScope => $"pos.estimate.convert:{EstimateId}";
    public bool IdempotencyRequired => true;
}

public record ConvertEstimateResult(
    Guid NewSaleId,
    string InvoiceNumber,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Balance);
