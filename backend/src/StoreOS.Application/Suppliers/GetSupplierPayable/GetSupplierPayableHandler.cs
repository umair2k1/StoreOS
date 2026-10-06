using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.GetSupplierPayable;

public class GetSupplierPayableHandler : IRequestHandler<GetSupplierPayableQuery, SupplierPayableDto>
{
    private readonly IAppDbContext _db;
    private readonly ISupplierLedgerService _ledgerService;

    public GetSupplierPayableHandler(IAppDbContext db, ISupplierLedgerService ledgerService)
    {
        _db = db;
        _ledgerService = ledgerService;
    }

    public async Task<SupplierPayableDto> Handle(GetSupplierPayableQuery request, CancellationToken ct)
    {
        var supplierExists = await _db.Set<Supplier>()
            .AnyAsync(s => s.Id == request.SupplierId, ct);
        if (!supplierExists)
        {
            throw new DomainException("supplier_not_found", $"Supplier '{request.SupplierId}' was not found.");
        }

        var balance = await _ledgerService.GetPayableAsync(request.SupplierId, ct);
        return new SupplierPayableDto(request.SupplierId, balance);
    }
}
