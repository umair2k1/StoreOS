using FluentAssertions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Domain.Tests.Tenancy;

public class TenantTests
{
    [Fact]
    public void Create_ValidName_CreatesActiveTenantWithDefaultTimezoneAndSlug()
    {
        var tenant = Tenant.Create("Apex Retail");

        tenant.Id.Should().NotBeEmpty();
        tenant.Name.Should().Be("Apex Retail");
        tenant.Subdomain.Should().StartWith("apex-retail-");
        tenant.IsActive.Should().BeTrue();
        tenant.Timezone.Should().Be("Asia/Kolkata");
        tenant.FeaturesJson.Should().Be("{}");
        tenant.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_CustomSubdomain_SetsGivenSubdomain()
    {
        var tenant = Tenant.Create("Apex Retail", subdomain: "apex");

        tenant.Subdomain.Should().Be("apex");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NullOrEmptyName_ThrowsArgumentException(string? invalidName)
    {
        var act = () => Tenant.Create(invalidName!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_ValidName_UpdatesName()
    {
        var tenant = Tenant.Create("Original Name");
        tenant.Rename("Updated Name");

        tenant.Name.Should().Be("Updated Name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_InvalidName_ThrowsArgumentException(string? invalidName)
    {
        var tenant = Tenant.Create("Original Name");
        var act = () => tenant.Rename(invalidName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetSubdomain_ValidSubdomain_UpdatesSubdomain()
    {
        var tenant = Tenant.Create("Apex");
        tenant.SetSubdomain("apex-store");

        tenant.Subdomain.Should().Be("apex-store");
    }

    [Theory]
    [InlineData("ApexStore")] // uppercase forbidden
    [InlineData("-apex")] // leading hyphen forbidden
    [InlineData("apex-")] // trailing hyphen forbidden
    [InlineData("apex_store")] // underscore forbidden
    [InlineData("")]
    public void SetSubdomain_InvalidSubdomain_ThrowsArgumentException(string invalidSubdomain)
    {
        var tenant = Tenant.Create("Apex");
        var act = () => tenant.SetSubdomain(invalidSubdomain);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ActivateAndDeactivate_TogglesStatus()
    {
        var tenant = Tenant.Create("Apex");
        tenant.IsActive.Should().BeTrue();

        tenant.Deactivate();
        tenant.IsActive.Should().BeFalse();

        tenant.Activate();
        tenant.IsActive.Should().BeTrue();
    }

    [Fact]
    public void SetFeatures_UpdatesFeaturesJson()
    {
        var tenant = Tenant.Create("Apex");
        tenant.SetFeatures("{\"repairs\":true}");

        tenant.FeaturesJson.Should().Be("{\"repairs\":true}");
    }
}
