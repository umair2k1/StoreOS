using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.GetSupplierTransactions;

public class GetSupplierTransactionsHandler : IRequestHandler<GetSupplierTransactionsQuery, PagedResult<SupplierTransactionDto>>
{
    private readonly IAppDbContext _db;

    public GetSupplierTransactionsHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResult<SupplierTransactionDto>> Handle(GetSupplierTransactionsQuery request, CancellationToken ct)
    {
        var supplierExists = await _db.Set<Supplier>()
            .AnyAsync(s => s.Id == request.SupplierId, ct);
        if (!supplierExists)
        {
            throw new DomainException("supplier_not_found", $"Supplier '{request.SupplierId}' was not found.");
        }

        var query = _db.Set<SupplierTransaction>()
            .AsNoTracking()
            .Where(t => t.SupplierId == request.SupplierId);

        if (request.FromDate.HasValue)
        {
            query = query.Where(t => t.TxnDate >= request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            query = query.Where(t => t.TxnDate <= request.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            query = query.Where(t => t.Type == request.Type);
        }

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : Math.Min(request.PageSize, 100);

        var items = await query
            .OrderByDescending(t => t.TxnDate)
            .ThenByDescending(t => t.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new SupplierTransactionDto(
                t.Id,
                t.SupplierId,
                t.BranchId,
                t.Type,
                t.Amount,
                t.PaymentMode,
                t.ReferenceNo,
                t.TxnDate,
                t.Notes,
                t.CreatedBy,
                t.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<SupplierTransactionDto>(items, totalCount, page, pageSize);
    }
}
