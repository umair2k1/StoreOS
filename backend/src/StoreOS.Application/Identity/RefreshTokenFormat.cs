using System.Security.Cryptography;
using System.Text;

namespace StoreOS.Application.Identity;

public static class RefreshTokenFormat
{
    // raw token = "<tenantId as 32 hex chars, no dashes>.<base64url(32 random bytes)>"
    public static string Compose(Guid tenantId, byte[] secret) =>
        $"{tenantId:N}.{Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    public static bool TryParseTenantId(string? raw, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var dot = raw.IndexOf('.');
        return dot == 32 && Guid.TryParseExact(raw[..32], "N", out tenantId);
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
