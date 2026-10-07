using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.GetReturnById;

public class GetSupplierReturnByIdHandler : IRequestHandler<GetSupplierReturnByIdQuery, SupplierReturnDto>
{
    private readonly IAppDbContext _db;

    public GetSupplierReturnByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<SupplierReturnDto> Handle(GetSupplierReturnByIdQuery request, CancellationToken ct)
    {
        var ret = await _db.Set<SupplierReturn>()
            .Include(r => r.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == request.Id, ct);

        if (ret == null)
        {
            throw new DomainException("supplier_return_not_found", $"Supplier return '{request.Id}' was not found.");
        }

        var supplier = await _db.Set<Supplier>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == ret.SupplierId, ct);

        var items = ret.Items.Select(i => new SupplierReturnItemDto(
            i.Id,
            i.ItemName,
            i.Qty,
            i.ReturnRate,
            i.LineTotal,
            i.ImeiNumbers
        )).ToList();

        return new SupplierReturnDto(
            ret.Id,
            ret.TenantId,
            ret.BranchId,
            ret.ReturnNumber,
            ret.ReturnDate,
            ret.SupplierId,
            supplier?.Name ?? "Supplier",
            ret.SupplierPhone ?? supplier?.Phone,
            ret.SupplierAddress ?? supplier?.Address,
            ret.TotalQty,
            ret.TotalAmount,
            ret.SettlementType,
            ret.Reason,
            ret.Notes,
            items
        );
    }
}
