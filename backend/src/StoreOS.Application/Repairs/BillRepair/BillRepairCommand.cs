using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Checkout;

namespace StoreOS.Application.Repairs.BillRepair;

public record BillRepairCommand(
    Guid RepairJobId,
    IReadOnlyList<CheckoutPayment>? Payments = null)
    : IRequest<BillRepairResult>, IRequiresPermission, IIdempotentRequest, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
    public string? IdempotencyKey { get; init; }
    public string IdempotencyScope => $"repairs.bill:{RepairJobId}";
    public bool IdempotencyRequired => true;
}

public record BillRepairResult(Guid SaleId, string InvoiceNumber, decimal GrandTotal, decimal AmountPaid, decimal Balance);
