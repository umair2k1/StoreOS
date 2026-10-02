using System.Text.RegularExpressions;
using FluentAssertions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Domain.Tests.Tenancy;

public class SlugTests
{
    private static readonly Regex SubdomainPattern = new(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromName_NullOrWhitespace_ThrowsArgumentException(string? name)
    {
        var act = () => Slug.FromName(name!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FromName_StandardName_GeneratesCleanSlugWithHexSuffix()
    {
        var slug = Slug.FromName("Apex Stores");

        slug.Should().StartWith("apex-stores-");
        slug.Length.Should().Be("apex-stores-".Length + 4);
        SubdomainPattern.IsMatch(slug).Should().BeTrue();
    }

    [Fact]
    public void FromName_SpecialCharactersAndSpaces_ReplacedWithHyphens()
    {
        var slug = Slug.FromName("Apex & Co. Store #1!");

        slug.Should().StartWith("apex-co-store-1-");
        SubdomainPattern.IsMatch(slug).Should().BeTrue();
    }

    [Fact]
    public void FromName_OnlySpecialCharacters_FallsBackToTenant()
    {
        var slug = Slug.FromName("$$$%%%^^^");

        slug.Should().StartWith("tenant-");
        SubdomainPattern.IsMatch(slug).Should().BeTrue();
    }

    [Fact]
    public void FromName_VeryLongName_TruncatesBaseTo40Chars()
    {
        var longName = "A Very Long Store Name That Definitely Exceeds The Forty Character Limit";
        var slug = Slug.FromName(longName);

        var hexPart = slug[^4..];
        var basePart = slug[..^5]; // Exclude "-xxxx"

        basePart.Length.Should().BeLessThanOrEqualTo(40);
        basePart.Should().NotEndWith("-");
        SubdomainPattern.IsMatch(slug).Should().BeTrue();
    }

    [Fact]
    public void FromName_MultipleInvocations_ProducesDifferentHexSuffixes()
    {
        var slug1 = Slug.FromName("Apex Electronics");
        var slug2 = Slug.FromName("Apex Electronics");

        slug1.Should().NotBe(slug2);
        slug1.Should().StartWith("apex-electronics-");
        slug2.Should().StartWith("apex-electronics-");
    }
}
