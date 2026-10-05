namespace StoreOS.Domain.Tax;

public readonly record struct LineTax(decimal Taxable, decimal Cgst, decimal Sgst)
{
    public decimal TotalGst => Cgst + Sgst;
    public static LineTax Zero => new(0m, 0m, 0m);
}
