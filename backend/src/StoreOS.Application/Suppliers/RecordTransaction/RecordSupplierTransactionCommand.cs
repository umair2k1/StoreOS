using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.RecordTransaction;

public record RecordSupplierTransactionCommand(
    Guid SupplierId,
    string Type,
    decimal Amount,
    string? PaymentMode = null,
    string? ReferenceNo = null,
    DateOnly TxnDate = default,
    string? Notes = null) : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}
