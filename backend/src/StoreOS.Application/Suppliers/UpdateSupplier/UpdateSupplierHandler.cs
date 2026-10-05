using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.UpdateSupplier;

public class UpdateSupplierHandler : IRequestHandler<UpdateSupplierCommand>
{
    private readonly IAppDbContext _db;

    public UpdateSupplierHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateSupplierCommand request, CancellationToken ct)
    {
        var supplier = await _db.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == request.Id, ct);
        if (supplier == null)
        {
            throw new DomainException("supplier_not_found", $"Supplier '{request.Id}' was not found.");
        }

        supplier.UpdateProfile(
            request.Name,
            request.Phone,
            request.Address,
            request.BrandOrCategory,
            request.Notes);

        await _db.SaveChangesAsync(ct);
    }
}
