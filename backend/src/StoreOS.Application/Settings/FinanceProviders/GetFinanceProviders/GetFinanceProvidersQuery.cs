using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using System.Text.Json;

namespace StoreOS.Application.Settings.FinanceProviders.GetFinanceProviders;

public record GetFinanceProvidersQuery : IRequest<IReadOnlyList<FinanceProviderDto>>, IReadOnlyRequest;

public class GetFinanceProvidersHandler : IRequestHandler<GetFinanceProvidersQuery, IReadOnlyList<FinanceProviderDto>>
{
    private const string SettingKey = "finance_providers";
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetFinanceProvidersHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IReadOnlyList<FinanceProviderDto>> Handle(GetFinanceProvidersQuery request, CancellationToken ct)
    {
        var query = from s in _db.Set<Setting>().IgnoreQueryFilters().AsNoTracking()
                    join b in _db.Set<Branch>().IgnoreQueryFilters().AsNoTracking() on s.BranchId equals b.Id into branchGroup
                    from b in branchGroup.DefaultIfEmpty()
                    where s.TenantId == _scope.TenantId && b == null && s.Key == SettingKey
                    select s.ValueJson;

        var valueJson = await query.FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(valueJson))
        {
            return Array.Empty<FinanceProviderDto>();
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<FinanceProviderDto>>(valueJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return list ?? (IReadOnlyList<FinanceProviderDto>)Array.Empty<FinanceProviderDto>();
        }
        catch
        {
            return Array.Empty<FinanceProviderDto>();
        }
    }
}
