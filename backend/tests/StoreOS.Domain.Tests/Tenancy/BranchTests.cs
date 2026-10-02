using FluentAssertions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Domain.Tests.Tenancy;

public class BranchTests
{
    [Fact]
    public void Create_ValidCodeAndName_CreatesActiveBranch()
    {
        var tenantId = Guid.NewGuid();
        var branch = Branch.Create(tenantId, "B1", "Main Branch");

        branch.Id.Should().NotBeEmpty();
        branch.TenantId.Should().Be(tenantId);
        branch.Code.Should().Be("B1");
        branch.Name.Should().Be("Main Branch");
        branch.IsActive.Should().BeTrue();
        branch.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("HQ")]
    [InlineData("1")]
    [InlineData("B1")]
    [InlineData("A99")]
    [InlineData("Z")]
    public void Create_ValidBranchCodes_Succeeds(string validCode)
    {
        var branch = Branch.Create(Guid.NewGuid(), validCode, "Branch " + validCode);
        branch.Code.Should().Be(validCode);
    }

    [Theory]
    [InlineData("01")]   // Starts with 0
    [InlineData("0")]    // Starts with 0
    [InlineData("MAIN")] // 4 characters (> 3)
    [InlineData("B-1")]  // Special character
    [InlineData("b1 ")]  // Will be normalized to B1 which is valid, but let's test invalid after trim
    [InlineData("")]     // Empty
    public void Create_InvalidBranchCode_ThrowsArgumentException(string invalidCode)
    {
        if (invalidCode.Trim().ToUpperInvariant() == "B1")
            return; // Trimmed B1 is valid

        var act = () => Branch.Create(Guid.NewGuid(), invalidCode, "Test Branch");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_ThrowsArgumentException(string? invalidName)
    {
        var act = () => Branch.Create(Guid.NewGuid(), "B1", invalidName!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateProfile_ValidInputs_UpdatesProperties()
    {
        var branch = Branch.Create(Guid.NewGuid(), "B1", "Main Branch");

        branch.UpdateProfile(
            name: "Flagship Store",
            address: "123 Market Street",
            phone: "+91 9876543210",
            email: "store@apex.com",
            gstin: "27AAAAA0000A1Z5");

        branch.Name.Should().Be("Flagship Store");
        branch.Address.Should().Be("123 Market Street");
        branch.Phone.Should().Be("+91 9876543210");
        branch.Email.Should().Be("store@apex.com");
        branch.Gstin.Should().Be("27AAAAA0000A1Z5");
    }

    [Fact]
    public void ActivateAndDeactivate_TogglesStatus()
    {
        var branch = Branch.Create(Guid.NewGuid(), "B1", "Main");
        branch.IsActive.Should().BeTrue();

        branch.Deactivate();
        branch.IsActive.Should().BeFalse();

        branch.Activate();
        branch.IsActive.Should().BeTrue();
    }
}
