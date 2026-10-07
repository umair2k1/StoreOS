using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Catalog;

public class Product
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public decimal SellPrice { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public decimal GstRate { get; private set; } = 18m;
    public bool IsTaxable { get; private set; } = true;
    public bool IsMarginScheme { get; private set; }
    public string? HsnCode { get; private set; }
    public string? Description { get; private set; }
    public int? EstimatedDurationMinutes { get; private set; }
    public int? WarrantyDays { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private Product() { }

    public static Product Create(Guid tenantId, string name, string category, decimal sellPrice,
        decimal purchasePrice, decimal gstRate, string? hsnCode)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        if (!ProductCategory.IsValid(category))
            throw new DomainValidationException("invalid_product_category", $"Unknown category '{category}'.");
        if (sellPrice < 0) throw new DomainValidationException("invalid_sell_price", "Sell price cannot be negative.");
        if (purchasePrice < 0) throw new DomainValidationException("invalid_purchase_price", "Purchase price cannot be negative.");
        if (gstRate < 0) throw new DomainValidationException("invalid_gst_rate", "GST rate cannot be negative.");

        var product = new Product
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Category = category,
            SellPrice = sellPrice, PurchasePrice = purchasePrice, GstRate = gstRate, HsnCode = hsnCode,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (category == ProductCategory.RepairService)
        {
            product.HsnCode ??= "998713";
            product.GstRate = gstRate == 0m ? 18m : gstRate;
            product.EstimatedDurationMinutes = 60;
            product.WarrantyDays = 30;
        }
        return product;
    }

    public void SetMarginScheme(bool enabled)
    {
        if (enabled && Category != ProductCategory.UsedPhone)
            throw new DomainValidationException("margin_scheme_invalid_category",
                "Margin scheme can only be enabled for Used Phone items.");
        IsMarginScheme = enabled;
    }

    public void UpdatePricing(decimal sellPrice, decimal purchasePrice, decimal gstRate)
    {
        if (sellPrice < 0 || purchasePrice < 0 || gstRate < 0)
            throw new DomainValidationException("invalid_pricing", "Prices and GST rate cannot be negative.");
        SellPrice = sellPrice; PurchasePrice = purchasePrice; GstRate = gstRate;
    }

    public void UpdateDescriptionAndServiceFields(string? description, int? estimatedDurationMinutes, int? warrantyDays)
    {
        Description = description;
        if (Category == ProductCategory.RepairService)
        {
            EstimatedDurationMinutes = estimatedDurationMinutes ?? 60;
            WarrantyDays = warrantyDays ?? 30;
        }
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetTaxable(bool isTaxable) => IsTaxable = isTaxable;

    public void SetHsnCode(string? hsnCode) => HsnCode = hsnCode;

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
