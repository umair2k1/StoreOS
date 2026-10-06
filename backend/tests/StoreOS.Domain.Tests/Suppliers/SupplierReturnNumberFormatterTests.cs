using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Domain.Tests.Suppliers;

public class SupplierReturnNumberFormatterTests
{
    [Theory]
    [InlineData("BR01", 2026, 10, 7, 1, "RET-BR01-20261007-001")]
    [InlineData("main", 2026, 3, 15, 42, "RET-MAIN-20260315-042")]
    [InlineData("HYD", 2026, 12, 31, 999, "RET-HYD-20261231-999")]
    [InlineData("  blr-02 ", 2027, 1, 1, 5, "RET-BLR-02-20270101-005")]
    public void Format_ProducesExpectedShape(string branchCode, int year, int month, int day, long seq, string expected)
    {
        var date = new DateOnly(year, month, day);
        var formatted = SupplierReturnNumberFormatter.Format(branchCode, date, seq);
        formatted.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Format_NonPositiveSequence_ThrowsDomainValidationException(long invalidSeq)
    {
        Action act = () => SupplierReturnNumberFormatter.Format("BR01", new DateOnly(2026, 10, 7), invalidSeq);
        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "sequence_invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Format_EmptyBranchCode_ThrowsDomainValidationException(string? invalidBranch)
    {
        Action act = () => SupplierReturnNumberFormatter.Format(invalidBranch!, new DateOnly(2026, 10, 7), 1);
        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "branch_code_required");
    }
}
