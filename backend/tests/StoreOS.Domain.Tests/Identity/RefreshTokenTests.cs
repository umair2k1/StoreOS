using FluentAssertions;
using StoreOS.Domain.Identity;

namespace StoreOS.Domain.Tests.Identity;

public class RefreshTokenTests
{
    [Fact]
    public void IssueNew_ValidParameters_ReturnsActiveToken()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var tokenHash = "hash1234567890abcdef";
        var expiresAt = DateTimeOffset.UtcNow.AddDays(7);

        var token = RefreshToken.IssueNew(userId, tokenHash, familyId, expiresAt);

        token.Id.Should().NotBeEmpty();
        token.UserId.Should().Be(userId);
        token.TokenHash.Should().Be(tokenHash);
        token.FamilyId.Should().Be(familyId);
        token.ExpiresAt.Should().Be(expiresAt);
        token.IsRevoked.Should().BeFalse();
        token.ReplacedByTokenId.Should().BeNull();
        token.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IssueNew_EmptyTokenHash_ThrowsArgumentException(string? invalidHash)
    {
        var act = () => RefreshToken.IssueNew(Guid.NewGuid(), invalidHash!, Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7));
        act.Should().Throw<ArgumentException>()
            .WithParameterName("tokenHash");
    }

    [Fact]
    public void RotateTo_ActiveToken_MarksRevokedAndSetsReplacedBy()
    {
        var token = RefreshToken.IssueNew(Guid.NewGuid(), "hash", Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7));
        var nextTokenId = Guid.NewGuid();

        token.RotateTo(nextTokenId);

        token.IsRevoked.Should().BeTrue();
        token.ReplacedByTokenId.Should().Be(nextTokenId);
    }

    [Fact]
    public void RotateTo_AlreadyRevokedToken_ThrowsInvalidOperationException()
    {
        var token = RefreshToken.IssueNew(Guid.NewGuid(), "hash", Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7));
        token.Revoke();

        var act = () => token.RotateTo(Guid.NewGuid());
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Token already revoked.");
    }

    [Fact]
    public void Revoke_ActiveToken_SetsIsRevokedTrue()
    {
        var token = RefreshToken.IssueNew(Guid.NewGuid(), "hash", Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7));
        token.Revoke();

        token.IsRevoked.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_BeforeExpiration_ReturnsFalse()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var token = RefreshToken.IssueNew(Guid.NewGuid(), "hash", Guid.NewGuid(), expiresAt);

        token.IsExpired(DateTimeOffset.UtcNow).Should().BeFalse();
    }

    [Fact]
    public void IsExpired_AtOrAfterExpiration_ReturnsTrue()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var token = RefreshToken.IssueNew(Guid.NewGuid(), "hash", Guid.NewGuid(), expiresAt);

        token.IsExpired(DateTimeOffset.UtcNow).Should().BeTrue();
    }
}
