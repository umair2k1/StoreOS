using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Api.Controllers;

public record ResolvedShopBranding(
    string ShopName,
    string? Address,
    string? Phone,
    string? Email,
    string? Gstin,
    byte[]? LogoBytes = null
) : StoreOS.Infrastructure.Printing.ShopBranding(ShopName, Address, Phone, Email, Gstin, LogoBytes);

public static class ShopBrandingHelper
{
    public static async Task<ResolvedShopBranding> ResolveAsync(
        Guid? branchId,
        IAppDbContext db,
        CancellationToken ct,
        IObjectStorageService? storage = null,
        Guid? tenantId = null)
    {
        var tx = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        try
        {
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                await db.ExecuteRawSqlAsync(
                    "SELECT set_config('app.tenant_id', {0}, true)",
                    new object[] { tenantId.Value.ToString() },
                    ct);
            }

            Branch? branch = null;
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                branch = await db.Set<Branch>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(b => b.Id == branchId.Value, ct);

                if (branch != null && !tenantId.HasValue)
                {
                    tenantId = branch.TenantId;
                    await db.ExecuteRawSqlAsync(
                        "SELECT set_config('app.tenant_id', {0}, true)",
                        new object[] { tenantId.Value.ToString() },
                        ct);
                }
            }

            Setting? setting = null;
            if (branchId.HasValue && branchId.Value != Guid.Empty)
            {
                setting = await db.Set<Setting>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.BranchId == branchId.Value && s.Key == "shop_profile", ct);

                setting ??= await db.Set<Setting>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.BranchId == branchId.Value && s.Key == "shop_info", ct);
            }

            if (setting == null && tenantId.HasValue)
            {
                setting = await db.Set<Setting>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.BranchId == null && s.Key == "shop_profile", ct);

                setting ??= await db.Set<Setting>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.BranchId == null && s.Key == "shop_info", ct);
            }

            Tenant? tenant = null;
            if (tenantId.HasValue)
            {
                tenant = await db.Set<Tenant>()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == tenantId.Value, ct);
            }

            string? shopName = null;
            string? address = null;
            string? phone = null;
            string? email = null;
            string? gstin = null;
            byte[]? logoBytes = null;

            if (setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(setting.ValueJson);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("shopName", out var sn) || root.TryGetProperty("name", out sn))
                    {
                        shopName = sn.GetString();
                    }
                    if (root.TryGetProperty("address", out var addr))
                    {
                        address = addr.GetString();
                    }
                    if (root.TryGetProperty("phone", out var ph))
                    {
                        phone = ph.GetString();
                    }
                    if (root.TryGetProperty("email", out var em))
                    {
                        email = em.GetString();
                    }
                    if (root.TryGetProperty("gstin", out var gs))
                    {
                        gstin = gs.GetString();
                    }
                    if (root.TryGetProperty("logo", out var logoProp) || root.TryGetProperty("logoBase64", out logoProp))
                    {
                        var logoStr = logoProp.GetString();
                        if (!string.IsNullOrWhiteSpace(logoStr))
                        {
                            try
                            {
                                if (logoStr.StartsWith("data:image", StringComparison.OrdinalIgnoreCase))
                                {
                                    var commaIdx = logoStr.IndexOf(',');
                                    if (commaIdx >= 0) logoStr = logoStr[(commaIdx + 1)..];
                                }
                                var raw = Convert.FromBase64String(logoStr);
                                using var img = Image.Load(raw);
                                using var ms = new MemoryStream();
                                await img.SaveAsPngAsync(ms, ct);
                                logoBytes = ms.ToArray();
                            }
                            catch
                            {
                                // fallback on invalid base64
                            }
                        }
                    }
                }
                catch
                {
                    // Graceful fallback on JSON parse issues
                }
            }

            // Check object storage for tenant-uploaded logo if not already extracted from profile
            if (logoBytes == null && tenantId.HasValue && storage != null)
            {
                try
                {
                    var logoSetting = await db.Set<Setting>()
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.TenantId == tenantId.Value && s.BranchId == null && s.Key == "branding.logo_key", ct);

                    string? logoKey = null;
                    if (logoSetting != null && !string.IsNullOrWhiteSpace(logoSetting.ValueJson))
                    {
                        try
                        {
                            logoKey = JsonSerializer.Deserialize<string>(logoSetting.ValueJson);
                        }
                        catch
                        {
                            logoKey = logoSetting.ValueJson.Trim('"');
                        }
                    }

                    if (string.IsNullOrWhiteSpace(logoKey) && setting != null && !string.IsNullOrWhiteSpace(setting.ValueJson))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(setting.ValueJson);
                            if (doc.RootElement.TryGetProperty("logoUrl", out var lu))
                            {
                                var luStr = lu.GetString();
                                if (!string.IsNullOrWhiteSpace(luStr))
                                {
                                    logoKey = luStr.StartsWith("storeos-media/", StringComparison.OrdinalIgnoreCase)
                                        ? luStr["storeos-media/".Length..]
                                        : luStr;
                                }
                            }
                        }
                        catch
                        {
                            // fallback on invalid logoUrl
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(logoKey))
                    {
                        var stored = await storage.GetAsync("storeos-media", logoKey, ct);
                        if (stored != null && stored.Content.Length > 0)
                        {
                            using var img = Image.Load(stored.Content);
                            using var ms = new MemoryStream();
                            await img.SaveAsPngAsync(ms, ct);
                            logoBytes = ms.ToArray();
                        }
                    }
                }
                catch
                {
                    // Graceful fallback if storage is offline or key missing
                }
            }

            var resolvedShopName = !string.IsNullOrWhiteSpace(shopName)
                ? shopName
                : (!string.IsNullOrWhiteSpace(branch?.Name)
                    ? branch.Name
                    : (!string.IsNullOrWhiteSpace(tenant?.Name) ? tenant.Name : "StoreOS"));

            return new ResolvedShopBranding(
                ShopName: resolvedShopName,
                Address: !string.IsNullOrWhiteSpace(address) ? address : branch?.Address,
                Phone: !string.IsNullOrWhiteSpace(phone) ? phone : branch?.Phone,
                Email: !string.IsNullOrWhiteSpace(email) ? email : branch?.Email,
                Gstin: !string.IsNullOrWhiteSpace(gstin) ? gstin : branch?.Gstin,
                LogoBytes: logoBytes
            );
        }
        finally
        {
            if (tx != null)
            {
                await tx.DisposeAsync();
            }
        }
    }
}
