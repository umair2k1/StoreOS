using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Domain.Tests.Sales;

public class InvoiceNumberFormatterTests
{
    [Fact]
    public void Case1_FormatFyConsecutive_StandardBranchAndSequence_ProducesExact16CharCompliantString()
    {
        // Character count breakdown:
        // "B01" (3) + "/" (1) + "26-27" (5) + "/" (1) + "000042" (6) = 16 characters exactly.
        // CGST Rule 46(b) mandates max 16 characters.
        const string expected = "B01/26-27/000042";

        var result = InvoiceNumberFormatter.FormatFyConsecutive("B01", "26-27", 42);

        result.Should().Be(expected);
        result.Length.Should().Be(16);
        result.Length.Should().BeLessThanOrEqualTo(16);
    }

    [Fact]
    public void Case2_FormatFyConsecutive_ZeroSequence_ThrowsDomainValidationException()
    {
        var act = () => InvoiceNumberFormatter.FormatFyConsecutive("B01", "26-27", 0);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("invoice_sequence_invalid");
        ex.Message.Should().Contain("Sequence must be a positive integer.");
    }

    [Fact]
    public void Case3_FormatFyConsecutive_NegativeSequence_ThrowsDomainValidationException()
    {
        var act = () => InvoiceNumberFormatter.FormatFyConsecutive("B01", "26-27", -5);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("invoice_sequence_invalid");
        ex.Message.Should().Contain("Sequence must be a positive integer.");
    }

    [Fact]
    public void Case4_FormatFyConsecutive_LongBranchCodeExceeding16Chars_ThrowsDomainValidationException()
    {
        // "BRANCH01" (8) + "/" (1) + "26-27" (5) + "/" (1) + "123456" (6) = 21 characters (> 16 limit)
        var act = () => InvoiceNumberFormatter.FormatFyConsecutive("BRANCH01", "26-27", 123456);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("invoice_number_exceeds_rule46b");
        ex.Message.Should().Contain("exceeds the 16-character limit mandated by CGST Rule 46(b)");
    }

    [Fact]
    public void Case5_FormatDailyTimestamp_ValidInputs_ProducesExpected23CharString()
    {
        // "B01" (3) + "-" (1) + "20260923-143005" (15) + "-" (1) + "007" (3) = 23 characters
        var timestamp = new DateTimeOffset(2026, 9, 23, 14, 30, 5, TimeSpan.Zero);

        var result = InvoiceNumberFormatter.FormatDailyTimestamp("B01", timestamp, 7);

        result.Should().Be("B01-20260923-143005-007");
        result.Length.Should().Be(23);
    }

    [Fact]
    public void Case6_FinancialYearLabel_SeptemberDate_ReturnsCurrentFiscalYear()
    {
        var date = new DateOnly(2026, 9, 23);

        var label = InvoiceNumberFormatter.FinancialYearLabel(date);

        label.Should().Be("26-27");
    }

    [Fact]
    public void Case7_FinancialYearLabel_FebruaryDate_ReturnsPriorCalendarYearFiscalYear()
    {
        // Indian FY runs Apr 1 - Mar 31. Feb 2027 belongs to FY 2026-27.
        var date = new DateOnly(2027, 2, 10);

        var label = InvoiceNumberFormatter.FinancialYearLabel(date);

        label.Should().Be("26-27");
    }

    [Fact]
    public void Case8_FinancialYearLabel_AprilFirst_RolloverToNewFiscalYear()
    {
        // Indian FY rolls over exactly on April 1.
        var date = new DateOnly(2027, 4, 1);

        var label = InvoiceNumberFormatter.FinancialYearLabel(date);

        label.Should().Be("27-28");
    }

    [Fact]
    public void RevisionNote_FormatEstimate_ProducesEstimateSeriesExemptFrom16CharCap()
    {
        // "B01" (3) + "/E/" (3) + "26-27" (5) + "/" (1) + "000007" (6) = 18 characters
        // Estimates use their own series (DECISIONS D-07) and are exempt from Rule 46(b)
        var result = InvoiceNumberFormatter.FormatEstimate("B01", "26-27", 7);

        result.Should().Be("B01/E/26-27/000007");
        result.Length.Should().Be(18);
    }

    [Fact]
    public void RevisionNote_FormatEstimate_InvalidSequence_ThrowsDomainValidationException()
    {
        var act = () => InvoiceNumberFormatter.FormatEstimate("B01", "26-27", 0);

        var ex = act.Should().ThrowExactly<DomainValidationException>().Which;
        ex.Code.Should().Be("invoice_sequence_invalid");
    }
}
