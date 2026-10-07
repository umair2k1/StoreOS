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
    public async Task<IActionResult> GetManifest([FromQuery] Guid? tenantId, CancellationToken ct)
    {
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

        var shortName = tenant.Name.Length > 12 ? tenant.Name[..12] : tenant.Name;
        var manifest = new
        {
            id = "/",
            name = tenant.Name,
            short_name = shortName,
            start_url = "/",
            scope = "/",
            display = "standalone",
            theme_color = "#1e293b",
            background_color = "#0f172a",
            icons = new[]
            {
                new { src = "/api/tenant/logo.png?size=192", sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = "/api/tenant/logo.png?size=512", sizes = "512x512", type = "image/png", purpose = "any" }
            }
        };

        return new JsonResult(manifest) { ContentType = "application/manifest+json" };
    }
}
