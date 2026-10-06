using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Suppliers;

public static class SupplierReturnNumberFormatter
{
    public static string Format(string branchCode, DateOnly date, long seq)
    {
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new DomainValidationException("branch_code_required", "Branch code is required.");
        if (seq <= 0)
            throw new DomainValidationException("sequence_invalid", "Sequence must be a positive integer.");

        var cleanBranch = branchCode.Trim().ToUpperInvariant();
        return $"RET-{cleanBranch}-{date:yyyyMMdd}-{seq:D3}";
    }

    public static string Format(string branchCode, DateTimeOffset date, long seq)
    {
        return Format(branchCode, DateOnly.FromDateTime(date.Date), seq);
    }
}
