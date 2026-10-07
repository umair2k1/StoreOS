using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Platform.LogoUpload;

public class UploadLogoHandler : IRequestHandler<UploadLogoCommand, UploadLogoResult>
{
    private static readonly Dictionary<string, byte[]> MagicBytes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        ["image/jpeg"] = new byte[] { 0xFF, 0xD8, 0xFF },
    };

    private readonly IObjectStorageService _storage;
    private readonly IAccessScope _scope;
    private readonly IAppDbContext _db;

    public UploadLogoHandler(IObjectStorageService storage, IAccessScope scope, IAppDbContext db)
    {
        _storage = storage;
        _scope = scope;
        _db = db;
    }

    public async Task<UploadLogoResult> Handle(UploadLogoCommand request, CancellationToken ct)
    {
        var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp" };
        if (!allowedTypes.Contains(request.DeclaredContentType, StringComparer.OrdinalIgnoreCase))
            throw new DomainValidationException("logo_invalid_type", "Only PNG, JPEG, and WebP images are allowed.");

        if (request.FileBytes.Length > 2 * 1024 * 1024)
            throw new DomainValidationException("logo_too_large", "Logo must be 2MB or smaller.");

        if (!MatchesMagicBytes(request.FileBytes, request.DeclaredContentType))
            throw new DomainValidationException("logo_magic_bytes_mismatch", "File content does not match its declared type.");

        using var image = Image.Load(request.FileBytes);
        image.Metadata.ExifProfile = null;
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(512, 512),
            Mode = ResizeMode.Max
        }));

        using var outputStream = new MemoryStream();
        await image.SaveAsWebpAsync(outputStream, ct);
        var processedBytes = outputStream.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(processedBytes))[..16].ToLowerInvariant();

        var key = $"tenants/{_scope.TenantId}/branding/logo_{hash}.webp";
        var url = await _storage.UploadAsync("storeos-media", key, processedBytes, "image/webp", ct);

        // Revision Note v2: persist object key in settings table under 'branding.logo_key'
        var jsonValue = JsonSerializer.Serialize(key);
        await _db.ExecuteRawSqlAsync(@"
            INSERT INTO settings (tenant_id, branch_id, key, value)
            VALUES ({0}, NULL, 'branding.logo_key', {1}::jsonb)
            ON CONFLICT (tenant_id, branch_id_key, key)
            DO UPDATE SET value = EXCLUDED.value",
            new object[] { _scope.TenantId, jsonValue },
            ct);

        return new UploadLogoResult(url);
    }

    private static bool MatchesMagicBytes(byte[] bytes, string contentType)
    {
        if (contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
        {
            return bytes.Length >= 12
                && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;
        }

        return MagicBytes.TryGetValue(contentType, out var sig)
            && bytes.Length >= sig.Length
            && bytes.Take(sig.Length).SequenceEqual(sig);
    }
}
