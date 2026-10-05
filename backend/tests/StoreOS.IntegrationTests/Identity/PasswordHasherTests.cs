using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Application.Identity;

namespace StoreOS.IntegrationTests.Identity;

public class PasswordHasherTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly IPasswordHasher _hasher;

    public PasswordHasherTests(WebApplicationFactory<Program> factory)
    {
        var scope = factory.Services.CreateScope();
        _hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    }

    [Fact]
    public void Hash_And_Verify_WithSamePlaintext_Succeeds()
    {
        var password = "SuperSecretPassword123!";
        var hash = _hasher.Hash(password);

        var result = _hasher.Verify(password, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void Verify_WithDifferentPlaintext_Fails()
    {
        var password = "SuperSecretPassword123!";
        var wrongPassword = "WrongPassword123!";
        var hash = _hasher.Hash(password);

        var result = _hasher.Verify(wrongPassword, hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void Hash_SamePlaintextTwice_ProducesDifferentHashes()
    {
        var password = "SuperSecretPassword123!";

        var hash1 = _hasher.Hash(password);
        var hash2 = _hasher.Hash(password);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Hash_DoesNotContainPlaintextPassword()
    {
        var password = "SuperSecretPassword123!";

        var hash = _hasher.Hash(password);

        hash.Should().NotContain(password);
    }

    [Fact]
    public void Verify_MalformedOrGarbageStoredHash_ReturnsFalseGracefully()
    {
        var password = "SuperSecretPassword123!";
        var malformedHashes = new[]
        {
            "not-a-valid-hash",
            "argon2id$3$65536$4$invalidbase64!$invalidbase64!",
            "argon2id$invalid$memory$parallelism$c2FsdA==$aGFzaA==",
            "argon2id$3$65536$4",
            "",
            "unknown$3$65536$4$c2FsdA==$aGFzaA=="
        };

        foreach (var malformed in malformedHashes)
        {
            var action = () => _hasher.Verify(password, malformed);
            action.Should().NotThrow();
            _hasher.Verify(password, malformed).Should().BeFalse();
        }
    }
}
