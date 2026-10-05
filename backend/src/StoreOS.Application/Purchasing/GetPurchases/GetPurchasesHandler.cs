using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Purchasing;

namespace StoreOS.Application.Purchasing.GetPurchases;

public class GetPurchasesHandler : IRequestHandler<GetPurchasesQuery, PagedResult<PurchaseDto>>
{
    private readonly IAppDbContext _db;

    public GetPurchasesHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<PurchaseDto>> Handle(GetPurchasesQuery request, CancellationToken ct)
    {
        var query = _db.Set<Purchase>().AsNoTracking();

        if (request.ProductId.HasValue)
        {
            query = query.Where(p => p.ProductId == request.ProductId.Value);
        }

        if (request.BranchId.HasValue)
        {
            query = query.Where(p => p.BranchId == request.BranchId.Value);
        }

        if (request.FromDate.HasValue)
        {
            query = query.Where(p => p.PurchaseDate >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(p => p.PurchaseDate <= request.ToDate.Value);
        }

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : request.PageSize;

        var items = await query
            .OrderByDescending(p => p.PurchaseDate)
            .ThenByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PurchaseDto(
                p.Id,
                p.BranchId,
                p.ProductId,
                p.Qty,
                p.PurchaseRate,
                p.SupplierId,
                p.SupplierName,
                p.SupplierPhone,
                p.SupplierAddress,
                p.SellerName,
                p.SellerPhone,
                p.SellerAddress,
                p.SupplierInvoiceNo,
                p.PurchaseDate,
                p.CreatedBy,
                p.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<PurchaseDto>(items, totalCount, page, pageSize);
    }
}
