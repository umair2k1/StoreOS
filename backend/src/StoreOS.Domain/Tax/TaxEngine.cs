using StoreOS.Domain.Common;

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
}
