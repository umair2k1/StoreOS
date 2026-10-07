namespace StoreOS.Domain.Tenancy;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Subdomain { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;
    public string Timezone { get; private set; } = "Asia/Kolkata";
    public string FeaturesJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name, string timezone = "Asia/Kolkata", string? subdomain = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Subdomain = subdomain ?? Slug.FromName(name),
            Timezone = timezone,
            FeaturesJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Tenant Rehydrate(Guid id, string name, string subdomain, bool isActive, string timezone, string featuresJson, DateTimeOffset createdAt)
    {
        return new Tenant
        {
            Id = id,
            Name = name,
            Subdomain = subdomain,
            IsActive = isActive,
            Timezone = timezone,
            FeaturesJson = featuresJson,
            CreatedAt = createdAt
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetSubdomain(string subdomain)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain ?? "", "^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            throw new ArgumentException("Subdomain must be lowercase letters, digits and hyphens.", nameof(subdomain));
        Subdomain = subdomain!;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void SetFeatures(string featuresJson) => FeaturesJson = featuresJson;
}
