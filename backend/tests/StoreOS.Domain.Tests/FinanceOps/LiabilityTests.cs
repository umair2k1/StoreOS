using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Domain.Tests.FinanceOps;

public class LiabilityTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Theory]
    [InlineData(LiabilityType.Loan)]
    [InlineData(LiabilityType.CreditCard)]
    [InlineData(LiabilityType.Financing)]
    [InlineData(LiabilityType.Investment)]
    public void Liability_Create_ValidTypes_ShouldSucceed(string validCategory)
    {
        var liability = Liability.Create(
            _tenantId,
            "HDFC Business Loan",
            validCategory,
            "HDFC Bank",
            500000m,
            new DateOnly(2027, 3, 31),
            "Term: 24 months");

        liability.Title.Should().Be("HDFC Business Loan");
        liability.Type.Should().Be(validCategory);
        liability.LenderName.Should().Be("HDFC Bank");
        liability.TotalPrincipal.Should().Be(500000m);
        liability.DueDate.Should().Be(new DateOnly(2027, 3, 31));
        liability.Status.Should().Be("Active");
    }

    [Theory]
    [InlineData("Mortgage")]
    [InlineData("Overdraft")]
    [InlineData("")]
    public void Liability_Create_InvalidType_ShouldThrowDomainValidationException(string invalidType)
    {
        Action act = () => Liability.Create(
            _tenantId,
            "Unknown Facility",
            invalidType,
            "Private Lender",
            100000m,
            null,
            null);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "invalid_liability_type");
    }

    [Fact]
    public void Liability_UpdateDescriptiveData_NeverChangesTotalPrincipal()
    {
        var liability = Liability.Create(
            _tenantId,
            "SBI Term Loan",
            LiabilityType.Loan,
            "State Bank of India",
            250000m,
            new DateOnly(2026, 12, 31),
            "Initial notes");

        var initialPrincipal = liability.TotalPrincipal;
        initialPrincipal.Should().Be(250000m);

        // Update descriptive data
        liability.UpdateDescriptiveData(
            "SBI Term Loan - Restructured",
            "SBI Commercial Branch",
            new DateOnly(2027, 6, 30),
            "Restructured",
            "Tenure extended by 6 months");

        // Assert TotalPrincipal is strictly unchanged
        liability.TotalPrincipal.Should().Be(initialPrincipal);
        liability.TotalPrincipal.Should().Be(250000m);
        liability.Title.Should().Be("SBI Term Loan - Restructured");
        liability.LenderName.Should().Be("SBI Commercial Branch");
        liability.DueDate.Should().Be(new DateOnly(2027, 6, 30));
        liability.Status.Should().Be("Restructured");
        liability.Notes.Should().Be("Tenure extended by 6 months");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void Liability_Create_NonPositivePrincipal_ShouldThrowDomainValidationException(decimal invalidPrincipal)
    {
        Action act = () => Liability.Create(
            _tenantId,
            "Personal Loan",
            LiabilityType.Loan,
            "Family",
            invalidPrincipal,
            null,
            null);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "invalid_principal");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Liability_Create_EmptyTitle_ShouldThrowDomainValidationException(string? invalidTitle)
    {
        Action act = () => Liability.Create(
            _tenantId,
            invalidTitle!,
            LiabilityType.Loan,
            "Bank",
            50000m,
            null,
            null);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "liability_title_required");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Liability_UpdateDescriptiveData_EmptyTitle_ShouldThrowDomainValidationException(string? invalidTitle)
    {
        var liability = Liability.Create(
            _tenantId,
            "Valid Title",
            LiabilityType.Loan,
            "Bank",
            50000m,
            null,
            null);

        Action act = () => liability.UpdateDescriptiveData(invalidTitle!, "Bank", null, "Active", null);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "liability_title_required");
    }
}
