using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.GetSupplierById;

public class GetSupplierByIdHandler : IRequestHandler<GetSupplierByIdQuery, SupplierDto>
{
    private readonly IAppDbContext _db;

    public GetSupplierByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<SupplierDto> Handle(GetSupplierByIdQuery request, CancellationToken ct)
    {
        var supplier = await _db.Set<Supplier>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct);

        if (supplier == null)
        {
            throw new DomainException("supplier_not_found", $"Supplier '{request.Id}' was not found.");
        }

        return new SupplierDto(
            supplier.Id,
            supplier.Name,
            supplier.Phone,
            supplier.Address,
            supplier.BrandOrCategory,
            supplier.Notes,
            supplier.CreatedAt);
    }
}
