using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Tenancy.Branches.GetBranches;

public class GetBranchesHandler : IRequestHandler<GetBranchesQuery, IReadOnlyList<BranchDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetBranchesHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IReadOnlyList<BranchDto>> Handle(GetBranchesQuery request, CancellationToken ct)
    {
        var query = _db.Set<Branch>().AsQueryable();

        if (!_scope.AllBranches)
        {
            query = query.Where(b => _scope.BranchIds.Contains(b.Id));
        }

        var branches = await query
            .OrderBy(b => b.Code)
            .Select(b => new BranchDto(
                b.Id,
                b.Code,
                b.Name,
                b.Address,
                b.Phone,
                b.Email,
                b.Gstin,
                b.IsActive))
            .ToListAsync(ct);

        return branches;
    }
}
