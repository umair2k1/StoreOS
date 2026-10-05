namespace StoreOS.Domain.Sales;

public class SaleItem
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string ItemName { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public int Qty { get; private set; }
    public decimal PricePerUnit { get; private set; }
    public decimal? PurchasePriceSnapshot { get; private set; }
    public string? HsnSnapshot { get; private set; }
    public bool IsMarginScheme { get; private set; }
    public bool IsGift { get; private set; }
    public decimal Taxable { get; private set; }
    public decimal Cgst { get; private set; }
    public decimal Sgst { get; private set; }
    public string? Imei { get; private set; }

    private SaleItem() { }

    public static SaleItem Create(
        Guid tenantId,
        Guid saleId,
        Guid? productId,
        string itemName,
        string category,
        int qty,
        decimal pricePerUnit,
        decimal? purchasePriceSnapshot,
        string? hsnSnapshot,
        bool isMarginScheme,
        bool isGift,
        decimal taxable,
        decimal cgst,
        decimal sgst,
        string? imei)
    {
        return new SaleItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            SaleId = saleId,
            ProductId = productId,
            ItemName = itemName,
            Category = category,
            Qty = qty,
            PricePerUnit = pricePerUnit,
            PurchasePriceSnapshot = purchasePriceSnapshot,
            HsnSnapshot = hsnSnapshot,
            IsMarginScheme = isMarginScheme,
            IsGift = isGift,
            Taxable = taxable,
            Cgst = cgst,
            Sgst = sgst,
            Imei = imei
        };
    }
}
