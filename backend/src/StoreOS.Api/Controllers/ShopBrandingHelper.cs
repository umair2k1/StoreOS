using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Api.Controllers;

public record ResolvedShopBranding(
    string ShopName,
    string? Address,
    string? Phone,
    string? Email,
    string? Gstin
);

public static class ShopBrandingHelper
{
    public static async Task<ResolvedShopBranding> ResolveAsync(
        Guid branchId,
        IAppDbContext db,
        CancellationToken ct)
    {
        var branch = await db.Set<Branch>()
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == branchId, ct);

        var setting = await db.Set<Setting>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.BranchId == branchId && s.Key == "shop_profile", ct);

        string? shopName = null;
        string? address = null;
        string? phone = null;
        string? email = null;
        string? gstin = null;

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
            }
            catch
            {
                // Graceful fallback on JSON parse issues
            }
        }

        return new ResolvedShopBranding(
            ShopName: !string.IsNullOrWhiteSpace(shopName) ? shopName : (!string.IsNullOrWhiteSpace(branch?.Name) ? branch.Name : "StoreOS"),
            Address: !string.IsNullOrWhiteSpace(address) ? address : branch?.Address,
            Phone: !string.IsNullOrWhiteSpace(phone) ? phone : branch?.Phone,
            Email: !string.IsNullOrWhiteSpace(email) ? email : branch?.Email,
            Gstin: !string.IsNullOrWhiteSpace(gstin) ? gstin : branch?.Gstin
        );
    }
}
