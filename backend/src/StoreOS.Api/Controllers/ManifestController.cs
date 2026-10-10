using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;

namespace StoreOS.Api.Controllers;

[ApiController]
public class ManifestController : ControllerBase
{
    private readonly ITenantResolver _resolver;

    public ManifestController(ITenantResolver resolver)
    {
        _resolver = resolver;
    }

    [HttpGet("/api/tenant/manifest.webmanifest")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, VaryByHeader = "Host")]
    public async Task<IActionResult> GetManifest(
        [FromQuery] Guid? tenantId,
        [FromServices] IAppDbContext db,
        [FromServices] IObjectStorageService storage,
        [FromServices] IAccessScope scope,
        CancellationToken ct)
    {
        if ((!tenantId.HasValue || tenantId.Value == Guid.Empty) && scope.IsAuthenticated && scope.TenantId != Guid.Empty)
        {
            tenantId = scope.TenantId;
        }

        TenantPublicProfile? tenant = null;

        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            tenant = await _resolver.ResolveByIdAsync(tenantId.Value, ct);
        }
        else if (Request.Host.HasValue)
        {
            tenant = await _resolver.ResolveFromHostAsync(Request.Host.Value, ct);
        }

        if (tenant is null || !tenant.IsActive)
        {
            return NotFound();
        }

        var branding = await ShopBrandingHelper.ResolveAsync(null, db, ct, storage, tenant.TenantId);
        var effectiveName = !string.IsNullOrWhiteSpace(branding.ShopName) ? branding.ShopName : tenant.Name;
        var shortName = effectiveName.Length > 12 ? effectiveName[..12] : effectiveName;
        var manifest = new
        {
            id = "/",
            name = effectiveName,
            short_name = shortName,
            start_url = "/",
            scope = "/",
            display = "standalone",
            theme_color = "#1e293b",
            background_color = "#0f172a",
            icons = new[]
            {
                new { src = $"/api/tenant/logo.png?size=192&tenantId={tenant.TenantId}", sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = $"/api/tenant/logo.png?size=512&tenantId={tenant.TenantId}", sizes = "512x512", type = "image/png", purpose = "any" }
            }
        };

        return new JsonResult(manifest) { ContentType = "application/manifest+json" };
    }

    [HttpGet("/api/tenant/public-profile")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPublicProfile(
        [FromQuery] Guid? tenantId,
        [FromServices] IAppDbContext db,
        [FromServices] IObjectStorageService storage,
        [FromServices] IAccessScope scope,
        CancellationToken ct)
    {
        if ((!tenantId.HasValue || tenantId.Value == Guid.Empty) && scope.IsAuthenticated && scope.TenantId != Guid.Empty)
        {
            tenantId = scope.TenantId;
        }

        TenantPublicProfile? tenant = null;
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            tenant = await _resolver.ResolveByIdAsync(tenantId.Value, ct);
        }
        else if (Request.Host.HasValue)
        {
            tenant = await _resolver.ResolveFromHostAsync(Request.Host.Value, ct);
        }

        if (tenant is null || !tenant.IsActive)
        {
            return NotFound();
        }

        var branding = await ShopBrandingHelper.ResolveAsync(null, db, ct, storage, tenant.TenantId);
        var effectiveName = !string.IsNullOrWhiteSpace(branding.ShopName) ? branding.ShopName : tenant.Name;
        var hasLogo = branding.LogoBytes != null && branding.LogoBytes.Length > 0;

        return Ok(new
        {
            tenantId = tenant.TenantId,
            name = effectiveName,
            subdomain = tenant.Subdomain,
            logoUrl = hasLogo ? $"/api/tenant/logo.png?size=512&tenantId={tenant.TenantId}" : null
        });
    }
}
