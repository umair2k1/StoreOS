using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Sales;

public static class InvoiceNumberFormatter
{
    public static string FormatFyConsecutive(string branchCode, string financialYearLabel, int sequence)
    {
        // e.g. FormatFyConsecutive("B01", "26-27", 42) -> "B01/26-27/000042" (16 chars exactly)
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code is required.", nameof(branchCode));
        if (sequence <= 0)
            throw new DomainValidationException("invoice_sequence_invalid", "Sequence must be a positive integer.");
        var formatted = $"{branchCode}/{financialYearLabel}/{sequence:D6}";
        if (formatted.Length > 16)
            throw new DomainValidationException("invoice_number_exceeds_rule46b",
                $"Invoice number '{formatted}' exceeds the 16-character limit mandated by CGST Rule 46(b).");
        return formatted;
    }

    public static string FormatEstimate(string branchCode, string financialYearLabel, int sequence)
    {
        // e.g. FormatEstimate("B01", "26-27", 7) -> "B01/E/26-27/000007"
        // Estimates use their own series (DECISIONS D-07) and are exempt from the 16-character Rule 46(b) check.
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code is required.", nameof(branchCode));
        if (sequence <= 0)
            throw new DomainValidationException("invoice_sequence_invalid", "Sequence must be a positive integer.");
        return $"{branchCode}/E/{financialYearLabel}/{sequence:D6}";
    }

    public static string FormatDailyTimestamp(string branchCode, DateTimeOffset businessDateTime, int dailySequence)
    {
        // e.g. FormatDailyTimestamp("B01", 2026-09-23T14:30:05, 7) -> "B01-20260923-143005-007"
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code is required.", nameof(branchCode));
        if (dailySequence <= 0)
            throw new DomainValidationException("invoice_sequence_invalid", "Sequence must be a positive integer.");
        var formatted = $"{branchCode}-{businessDateTime:yyyyMMdd-HHmmss}-{dailySequence:D3}";
        return formatted;
        // Note: this format is NOT subject to the same 16-char cap check because Rule 46(b)
        // treats it as a "legacy/daily" series acknowledged to carry audit risk (see
        // Requirements §8.2's CA Confirmation Requirement) — it is intentionally longer and
        // permitted only via explicit tenant configuration + CA signoff, never the default.
    }

    public static string FinancialYearLabel(DateOnly businessDate)
    {
        // Indian financial year runs Apr 1 - Mar 31. E.g. 2026-09-23 -> "26-27";
        // 2027-02-10 -> "26-27"; 2027-04-01 -> "27-28".
        int fyStartYear = businessDate.Month >= 4 ? businessDate.Year : businessDate.Year - 1;
        int fyEndYearShort = (fyStartYear + 1) % 100;
        int fyStartYearShort = fyStartYear % 100;
        return $"{fyStartYearShort:D2}-{fyEndYearShort:D2}";
    }
}
