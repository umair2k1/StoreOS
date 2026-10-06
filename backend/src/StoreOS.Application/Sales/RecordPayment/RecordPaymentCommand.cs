using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.RecordPayment;

public record RecordPaymentCommand(
    Guid SaleId,
    decimal Amount,
    string PaymentMode
) : IRequest<RecordPaymentResult>, IRequiresPermission, ISerializableRequest, IIdempotentRequest
{
    public string RequiredPermission => "pos:checkout";
    public string? IdempotencyKey { get; init; }
    public string IdempotencyScope => $"sales.payment:{SaleId}";
    public bool IdempotencyRequired => true;
}

public record RecordPaymentResult(Guid PaymentId, decimal NewAmountPaid, decimal NewBalance);
