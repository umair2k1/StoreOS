using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StoreOS.Domain.Tenancy;

public static class Slug
{
    private static readonly Regex NonAlphanumericRegex = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public static string FromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required to generate a slug.", nameof(name));

        var lower = name.Trim().ToLowerInvariant();
        var hyphenated = NonAlphanumericRegex.Replace(lower, "-");
        var trimmed = hyphenated.Trim('-');

        if (string.IsNullOrEmpty(trimmed))
            trimmed = "tenant";

        if (trimmed.Length > 40)
            trimmed = trimmed[..40].TrimEnd('-');

        var randomBytes = RandomNumberGenerator.GetBytes(2);
        var hex = Convert.ToHexString(randomBytes).ToLowerInvariant();

        return $"{trimmed}-{hex}";
    }
}
