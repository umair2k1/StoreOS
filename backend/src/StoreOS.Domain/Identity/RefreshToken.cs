namespace StoreOS.Domain.Identity;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public bool IsRevoked { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RefreshToken() { }

    public static RefreshToken IssueNew(Guid userId, string tokenHash, Guid familyId, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        return new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = tokenHash, FamilyId = familyId,
            ExpiresAt = expiresAt, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void RotateTo(Guid newTokenId)
    {
        if (IsRevoked) throw new InvalidOperationException("Token already revoked.");
        IsRevoked = true;
        ReplacedByTokenId = newTokenId;
    }

    public void Revoke() => IsRevoked = true;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
