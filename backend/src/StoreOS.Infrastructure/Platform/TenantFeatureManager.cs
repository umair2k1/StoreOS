using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Platform;

public class TenantFeatureManager : IFeatureManager
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;

    public TenantFeatureManager(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<bool> IsRepairsEnabledAsync(CancellationToken ct)
    {
        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.Id == _scope.TenantId, ct);
        if (tenant == null) return false;

        if (string.IsNullOrWhiteSpace(tenant.FeaturesJson) || tenant.FeaturesJson == "{}")
            return false;

        using var doc = JsonDocument.Parse(tenant.FeaturesJson);
        return doc.RootElement.TryGetProperty("repairs", out var val) && val.GetBoolean();
    }

    public async Task SetRepairsEnabledAsync(bool enabled, CancellationToken ct)
    {
        var tenant = await _db.Tenants
            .FirstAsync(t => t.Id == _scope.TenantId, ct);

        var dict = (!string.IsNullOrWhiteSpace(tenant.FeaturesJson) && tenant.FeaturesJson != "{}"
            ? JsonSerializer.Deserialize<Dictionary<string, bool>>(tenant.FeaturesJson)
            : null) ?? new Dictionary<string, bool>();

        dict["repairs"] = enabled;
        tenant.SetFeatures(JsonSerializer.Serialize(dict));
        await _db.SaveChangesAsync(ct);
    }
}
