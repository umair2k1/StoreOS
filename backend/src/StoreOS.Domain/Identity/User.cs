namespace StoreOS.Domain.Identity;

public class User
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public int TokenVersion { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static User Create(Guid tenantId, string email, string passwordHash, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash required.", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name required.", nameof(displayName));
        return new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            DisplayName = displayName.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static User Rehydrate(Guid id, Guid tenantId, string email, string passwordHash, string displayName, int tokenVersion, bool isActive, DateTimeOffset createdAt)
    {
        return new User
        {
            Id = id,
            TenantId = tenantId,
            Email = email,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            TokenVersion = tokenVersion,
            IsActive = isActive,
            CreatedAt = createdAt
        };
    }

    public void ChangePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash)) throw new ArgumentException("Password hash required.", nameof(newHash));
        PasswordHash = newHash;
    }

    public void BumpTokenVersion() => TokenVersion++;
    public void Deactivate() { IsActive = false; BumpTokenVersion(); }
    public void Activate() => IsActive = true;
}
