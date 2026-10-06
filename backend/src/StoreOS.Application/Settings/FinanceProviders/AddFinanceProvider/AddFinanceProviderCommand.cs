using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;
using System.Text.Json;

namespace StoreOS.Application.Settings.FinanceProviders.AddFinanceProvider;

public record AddFinanceProviderCommand(string DisplayName) : IRequest<Unit>, IRequiresPermission
{
    public string RequiredPermission => "settings:manage";
}

public class AddFinanceProviderHandler : IRequestHandler<AddFinanceProviderCommand, Unit>
{
    private const string SettingKey = "finance_providers";
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public AddFinanceProviderHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Unit> Handle(AddFinanceProviderCommand request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new DomainValidationException("invalid_display_name", "Display name cannot be empty.");
        }

        var trimmedDisplay = request.DisplayName.Trim();
        var normalizedKey = trimmedDisplay.ToLowerInvariant();

        var query = from s in _db.Set<Setting>().IgnoreQueryFilters().AsNoTracking()
                    join b in _db.Set<Branch>().IgnoreQueryFilters().AsNoTracking() on s.BranchId equals b.Id into branchGroup
                    from b in branchGroup.DefaultIfEmpty()
                    where s.TenantId == _scope.TenantId && b == null && s.Key == SettingKey
                    select s.ValueJson;
        var existingValueJson = await query.FirstOrDefaultAsync(ct);

        List<FinanceProviderDto> list;
        if (existingValueJson == null)
        {
            list = new List<FinanceProviderDto> { new(normalizedKey, trimmedDisplay) };
            var newJson = JsonSerializer.Serialize(list);
            var rows = await _db.ExecuteRawSqlAsync(
                "INSERT INTO settings (tenant_id, branch_id, key, value) VALUES ({0}, NULL, {1}, {2}::jsonb)",
                new object[] { _scope.TenantId, SettingKey, newJson },
                ct);
            if (rows == 0)
            {
                throw new DomainException("setting_insert_failed", "Failed to insert setting row.");
            }
            return Unit.Value;
        }

        try
        {
            list = JsonSerializer.Deserialize<List<FinanceProviderDto>>(existingValueJson) ?? new List<FinanceProviderDto>();
        }
        catch
        {
            list = new List<FinanceProviderDto>();
        }

        // Case-insensitive duplicate check against EXISTING normalized keys
        // If a duplicate exists, this is a no-op (idempotent add, preserve existing display name)
        if (!list.Any(p => p.NormalizedName.Equals(normalizedKey, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(new FinanceProviderDto(normalizedKey, trimmedDisplay));
            var newJson = JsonSerializer.Serialize(list);
            await _db.ExecuteRawSqlAsync(
                "UPDATE settings SET value = {0}::jsonb WHERE tenant_id = {1} AND branch_id IS NULL AND key = {2}",
                new object[] { newJson, _scope.TenantId, SettingKey },
                ct);
        }
        return Unit.Value;
    }
}
