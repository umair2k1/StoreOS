using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.CreateSupplier;

public class CreateSupplierHandler : IRequestHandler<CreateSupplierCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public CreateSupplierHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(CreateSupplierCommand request, CancellationToken ct)
    {
        var supplier = Supplier.Create(
            _scope.TenantId,
            request.Name,
            request.Phone,
            request.Address,
            request.BrandOrCategory,
            request.Notes);

        await _db.Set<Supplier>().AddAsync(supplier, ct);
        await _db.SaveChangesAsync(ct);
        return supplier.Id;
    }
}
