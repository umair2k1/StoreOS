using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Settings.GetSetting;

public class GetSettingHandler : IRequestHandler<GetSettingQuery, string?>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetSettingHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<string?> Handle(GetSettingQuery request, CancellationToken ct)
    {
        var key = request.Key.Trim();

        if (request.BranchId == null)
        {
            var query = from s in _db.Set<Setting>().IgnoreQueryFilters().AsNoTracking()
                        join b in _db.Set<Branch>().IgnoreQueryFilters().AsNoTracking() on s.BranchId equals b.Id into branchGroup
                        from b in branchGroup.DefaultIfEmpty()
                        where s.TenantId == _scope.TenantId && b == null && s.Key == key
                        select s.ValueJson;

            return await query.FirstOrDefaultAsync(ct);
        }
        else
        {
            var query = from s in _db.Set<Setting>().IgnoreQueryFilters().AsNoTracking()
                        where s.TenantId == _scope.TenantId && s.BranchId == request.BranchId.Value && s.Key == key
                        select s.ValueJson;

            return await query.FirstOrDefaultAsync(ct);
        }
    }
}
