using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;

namespace StoreOS.Application.Repairs.Services.CreateRepairService;

public class CreateRepairServiceHandler : IRequestHandler<CreateRepairServiceCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public CreateRepairServiceHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(CreateRepairServiceCommand request, CancellationToken ct)
    {
        var product = Product.Create(
            _scope.TenantId,
            request.Name,
            ProductCategory.RepairService,
            request.StandardCharge,
            0m,
            request.GstRate ?? 18m,
            request.SacCode);

        product.UpdateDescriptionAndServiceFields(
            request.Description,
            request.EstimatedDurationMinutes ?? product.EstimatedDurationMinutes,
            request.WarrantyDays ?? product.WarrantyDays);

        product.SetTaxable(request.Taxable);

        if (request.GstRate.HasValue && request.GstRate.Value != product.GstRate)
        {
            product.UpdatePricing(request.StandardCharge, 0m, request.GstRate.Value);
        }

        await _db.Set<Product>().AddAsync(product, ct);
        await _db.SaveChangesAsync(ct);

        return product.Id;
    }
}
