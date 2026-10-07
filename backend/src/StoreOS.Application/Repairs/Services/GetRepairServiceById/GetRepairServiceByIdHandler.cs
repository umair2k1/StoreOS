using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Repairs.Services.GetRepairServiceById;

public class GetRepairServiceByIdHandler : IRequestHandler<GetRepairServiceByIdQuery, RepairServiceDto>
{
    private readonly IAppDbContext _db;

    public GetRepairServiceByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<RepairServiceDto> Handle(GetRepairServiceByIdQuery request, CancellationToken ct)
    {
        var product = await _db.Set<Product>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.Category == ProductCategory.RepairService, ct);

        if (product == null)
        {
            throw new DomainException("repair_service_not_found", $"Repair service '{request.Id}' was not found.");
        }

        return new RepairServiceDto(
            product.Id,
            product.Name,
            product.Description,
            product.SellPrice,
            product.IsTaxable,
            product.GstRate,
            product.HsnCode,
            product.EstimatedDurationMinutes,
            product.WarrantyDays,
            product.IsActive);
    }
}
