namespace StoreOS.Domain.Suppliers;

public class Supplier
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? BrandOrCategory { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Supplier() { }

    public static Supplier Create(Guid tenantId, string name, string? phone, string? address,
        string? brandOrCategory, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Supplier name is required.", nameof(name));
        return new Supplier
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Phone = phone,
            Address = address,
            BrandOrCategory = brandOrCategory,
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string name, string? phone, string? address, string? brandOrCategory, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Supplier name is required.", nameof(name));
        Name = name.Trim(); Phone = phone; Address = address; BrandOrCategory = brandOrCategory; Notes = notes;
    }
}
