namespace StoreOS.Domain.Tenancy;

public class Branch
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Gstin { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private Branch() { }

    public static Branch Create(Guid tenantId, string code, string name)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z1-9][A-Z0-9]{0,2}$"))
            throw new ArgumentException("Branch code must be 1-3 characters (A-Z, 0-9), not starting with 0.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is required.", nameof(name));
        return new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string name, string? address, string? phone, string? email, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is required.", nameof(name));
        Name = name.Trim();
        Address = address;
        Phone = phone;
        Email = email;
        Gstin = gstin;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
