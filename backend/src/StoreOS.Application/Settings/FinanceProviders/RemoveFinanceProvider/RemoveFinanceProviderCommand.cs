using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;
using System.Text.Json;

namespace StoreOS.Application.Settings.FinanceProviders.RemoveFinanceProvider;

public record RemoveFinanceProviderCommand(string NormalizedName) : IRequest<Unit>, IRequiresPermission
{
    public string RequiredPermission => "settings:manage";
}

public class RemoveFinanceProviderHandler : IRequestHandler<RemoveFinanceProviderCommand, Unit>
{
    private const string SettingKey = "finance_providers";
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public RemoveFinanceProviderHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Unit> Handle(RemoveFinanceProviderCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NormalizedName))
        {
            throw new DomainValidationException("invalid_normalized_name", "Normalized name cannot be empty.");
        }

        var normalizedKey = request.NormalizedName.Trim().ToLowerInvariant();

        var query = from s in _db.Set<Setting>().IgnoreQueryFilters().AsNoTracking()
                    join b in _db.Set<Branch>().IgnoreQueryFilters().AsNoTracking() on s.BranchId equals b.Id into branchGroup
                    from b in branchGroup.DefaultIfEmpty()
                    where s.TenantId == _scope.TenantId && b == null && s.Key == SettingKey
                    select s.ValueJson;
        var existingValueJson = await query.FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(existingValueJson))
        {
            return Unit.Value;
        }

        List<FinanceProviderDto> list;
        try
        {
            list = JsonSerializer.Deserialize<List<FinanceProviderDto>>(existingValueJson) ?? new List<FinanceProviderDto>();
        }
        catch
        {
            return Unit.Value;
        }

        var removedCount = list.RemoveAll(p => p.NormalizedName.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase));
        if (removedCount > 0)
        {
            var newJson = JsonSerializer.Serialize(list);
            await _db.ExecuteRawSqlAsync(
                "UPDATE settings SET value = {0}::jsonb WHERE tenant_id = {1} AND branch_id IS NULL AND key = {2}",
                new object[] { newJson, _scope.TenantId, SettingKey },
                ct);
        }

        return Unit.Value;
    }
}
