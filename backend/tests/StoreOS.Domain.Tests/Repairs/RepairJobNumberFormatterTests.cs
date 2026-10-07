using FluentAssertions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Domain.Tests.Repairs;

public class RepairJobNumberFormatterTests
{
    [Fact]
    public void Format_ProducesExpectedRepairJobNumber()
    {
        var branchCode = "BR1";
        var date = new DateOnly(2026, 10, 7);
        var seq = 42;

        var result = RepairJobNumberFormatter.Format(branchCode, date, seq);

        result.Should().Be("REP-BR1-20261007-0042");
    }

    [Fact]
    public void Format_SingleDigitSequence_PadsWithFourDigits()
    {
        var result = RepairJobNumberFormatter.Format("MAIN", new DateOnly(2026, 1, 1), 1);
        result.Should().Be("REP-MAIN-20260101-0001");
    }
}
