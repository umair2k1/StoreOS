using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Tax;

public static class TaxEngine
{
    public static LineTax StandardGst(decimal lineTotal, decimal gstRate)
    {
        if (lineTotal < 0) throw new ArgumentOutOfRangeException(nameof(lineTotal), "Line total cannot be negative.");
        if (gstRate < 0) throw new ArgumentOutOfRangeException(nameof(gstRate), "GST rate cannot be negative.");
        var taxable = Money.Round2(lineTotal / (1 + gstRate / 100m));
        var totalGst = Money.Round2(lineTotal - taxable);
        var cgst = Money.Round2(totalGst / 2m);
        var sgst = Money.Round2(totalGst - cgst);
        return new LineTax(taxable, cgst, sgst);
    }

    public static LineTax MarginSchemeGst(decimal sell, decimal cost, decimal gstRate)
    {
        // Hard domain validation: cost basis MUST be strictly positive.
        // Silent fallback to cost = 0 is prohibited (would treat full selling price as profit
        // margin, miscalculating GST and creating severe statutory audit liability under CGST
        // Rule 32(5)).
        if (cost <= 0)
            throw new DomainValidationException("margin_scheme_invalid_cost_basis",
                "Margin scheme requires a known positive cost basis (> 0). Cost cannot be zero or empty.");

        var margin = sell - cost;
        if (margin <= 0) return LineTax.Zero;
        var taxable = Money.Round2(margin / (1 + gstRate / 100m));
        var totalGst = Money.Round2(margin - taxable);
        var cgst = Money.Round2(totalGst / 2m);
        return new LineTax(taxable, cgst, Money.Round2(totalGst - cgst));
    }
}
