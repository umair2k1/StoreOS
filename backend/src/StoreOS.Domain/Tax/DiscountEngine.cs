using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Tax;

public static class DiscountEngine
{
    /// <summary>
    /// Applies a proportional invoice-level discount to a line's tax components.
    /// <paramref name="subtotal"/> represents the GROSS pre-discount invoice subtotal
    /// (the sum of gross line totals, i.e., unitPrice * qty across lines), consistent with CheckoutHandler (P-510).
    /// </summary>
    public static LineTax ApplyProportionalDiscount(LineTax before, decimal subtotal, decimal discount)
    {
        if (subtotal < 0) throw new ArgumentOutOfRangeException(nameof(subtotal), "Subtotal cannot be negative.");
        if (discount < 0)
            throw new DomainValidationException("discount_negative", "Discount cannot be negative.");
        if (discount > subtotal)
            throw new DomainValidationException("discount_exceeds_subtotal", "Discount cannot exceed subtotal.");
        if (subtotal == 0m)
        {
            // Nothing to discount proportionally against; factor is undefined -> treat as 1
            // (no-op) rather than dividing by zero, since a zero subtotal implies a zero
            // grand total regardless.
            return before;
        }
        var grand = subtotal - discount;
        var factor = grand / subtotal;
        var taxable = Money.Round2(before.Taxable * factor);
        var cgst = Money.Round2(before.Cgst * factor);
        var sgst = Money.Round2(before.Sgst * factor);
        return new LineTax(taxable, cgst, sgst);
    }
}
