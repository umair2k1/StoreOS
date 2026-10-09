using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Platform.LogoUpload;

public class GetTenantLogoHandler : IRequestHandler<GetTenantLogoQuery, LogoImage>
{
    private readonly IObjectStorageService _storage;
    private readonly IAppDbContext _db;

    public GetTenantLogoHandler(IObjectStorageService storage, IAppDbContext db)
    {
        _storage = storage;
        _db = db;
    }

    public async Task<LogoImage> Handle(GetTenantLogoQuery request, CancellationToken ct)
    {
        var size = request.Size;
        if (size != 192 && size != 512 && size != 1024)
        {
            size = 512;
        }

        // 1. Look for branding.logo_key setting for this tenant
        var query = from s in _db.Set<Setting>().IgnoreQueryFilters().AsNoTracking()
                    join b in _db.Set<Branch>().IgnoreQueryFilters().AsNoTracking() on s.BranchId equals b.Id into branchGroup
                    from b in branchGroup.DefaultIfEmpty()
                    where s.TenantId == request.TenantId && b == null && s.Key == "branding.logo_key"
                    select s.ValueJson;

        var settingJson = await query.FirstOrDefaultAsync(ct);

        string? logoKey = null;
        if (!string.IsNullOrWhiteSpace(settingJson))
        {
            try
            {
                logoKey = JsonSerializer.Deserialize<string>(settingJson);
            }
            catch
            {
                logoKey = settingJson.Trim('"');
            }
        }

        StoredObject? stored = null;
        if (!string.IsNullOrWhiteSpace(logoKey))
        {
            stored = await _storage.GetAsync("storeos-media", logoKey, ct);
        }

        if (stored != null && stored.Content.Length > 0)
        {
            // Process logo with ImageSharp: resize contain / pad transparent
            using var image = Image.Load(stored.Content);
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Pad,
                PadColor = Color.Transparent
            }));

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms, ct);
            var pngBytes = ms.ToArray();
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(pngBytes))[..16].ToLowerInvariant()}\"";

            return new LogoImage(pngBytes, "image/png", etag);
        }

        // Fallback: solid #1e293b square PNG generated in memory at requested size
        using (var fallback = new Image<Rgba32>(size, size, Color.ParseHex("#1e293b")))
        {
            using var ms = new MemoryStream();
            await fallback.SaveAsPngAsync(ms, ct);
            var fallbackBytes = ms.ToArray();
            var etag = $"\"{Convert.ToHexString(SHA256.HashData(fallbackBytes))[..16].ToLowerInvariant()}\"";

            return new LogoImage(fallbackBytes, "image/png", etag);
        }
    }
}
