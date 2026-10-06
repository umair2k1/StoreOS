using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.CreateReturn;

public record ReturnLineInput(
    Guid? ProductId,
    string ItemName,
    int Qty,
    decimal PurchasePrice,
    decimal ReturnRate,
    string[]? ImeiNumbers = null);

public record CreateSupplierReturnCommand(
    Guid SupplierId,
    DateOnly ReturnDate,
    IReadOnlyList<ReturnLineInput> Lines,
    string SettlementType,
    string? Reason = null,
    string? Notes = null)
    : IRequest<CreateSupplierReturnResult>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}

public record CreateSupplierReturnResult(Guid ReturnId, string ReturnNumber, decimal TotalAmount);
