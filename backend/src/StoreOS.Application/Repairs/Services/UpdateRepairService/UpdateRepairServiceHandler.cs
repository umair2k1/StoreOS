using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Repairs.Services.UpdateRepairService;

public class UpdateRepairServiceHandler : IRequestHandler<UpdateRepairServiceCommand>
{
    private readonly IAppDbContext _db;

    public UpdateRepairServiceHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(UpdateRepairServiceCommand request, CancellationToken ct)
    {
        var product = await _db.Set<Product>()
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.Category == ProductCategory.RepairService, ct);

        if (product == null)
        {
            throw new DomainException("repair_service_not_found", $"Repair service '{request.Id}' was not found.");
        }

        if (request.Name != null)
        {
            product.Rename(request.Name);
        }

        if (request.StandardCharge.HasValue || request.GstRate.HasValue)
        {
            product.UpdatePricing(
                request.StandardCharge ?? product.SellPrice,
                product.PurchasePrice,
                request.GstRate ?? product.GstRate);
        }

        if (request.Taxable.HasValue)
        {
            product.SetTaxable(request.Taxable.Value);
        }

        if (request.SacCode != null)
        {
            product.SetHsnCode(request.SacCode);
        }

        if (request.Description != null || request.EstimatedDurationMinutes.HasValue || request.WarrantyDays.HasValue)
        {
            product.UpdateDescriptionAndServiceFields(
                request.Description ?? product.Description,
                request.EstimatedDurationMinutes ?? product.EstimatedDurationMinutes,
                request.WarrantyDays ?? product.WarrantyDays);
        }

        if (request.IsActive.HasValue)
        {
            if (request.IsActive.Value)
            {
                product.Activate();
            }
            else
            {
                product.Deactivate();
            }
        }

        await _db.SaveChangesAsync(ct);
    }
}
