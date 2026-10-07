using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Repairs.Services.DeactivateRepairService;

public class DeactivateRepairServiceHandler : IRequestHandler<DeactivateRepairServiceCommand>
{
    private readonly IAppDbContext _db;

    public DeactivateRepairServiceHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task Handle(DeactivateRepairServiceCommand request, CancellationToken ct)
    {
        var product = await _db.Set<Product>()
            .FirstOrDefaultAsync(p => p.Id == request.Id && p.Category == ProductCategory.RepairService, ct);

        if (product == null)
        {
            throw new DomainException("repair_service_not_found", $"Repair service '{request.Id}' was not found.");
        }

        product.Deactivate();
        await _db.SaveChangesAsync(ct);
    }
}
