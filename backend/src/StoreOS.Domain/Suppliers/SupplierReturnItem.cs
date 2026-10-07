using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Suppliers;

public class SupplierReturnItem
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ReturnId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string ItemName { get; private set; } = default!;
    public int Qty { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public decimal ReturnRate { get; private set; }
    public decimal LineTotal { get; private set; }
    public string[]? ImeiNumbers { get; private set; }

    private SupplierReturnItem() { }

    public static SupplierReturnItem Create(
        Guid tenantId,
        Guid returnId,
        Guid? productId,
        string itemName,
        int qty,
        decimal purchasePrice,
        decimal returnRate,
        string[]? imeiNumbers = null)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            throw new DomainValidationException("invalid_item_name", "Item name is required.");

        if (qty <= 0)
            throw new DomainValidationException("invalid_qty", "Item quantity must be greater than zero.");

        if (purchasePrice < 0)
            throw new DomainValidationException("invalid_purchase_price", "Purchase price cannot be negative.");

        if (returnRate < 0)
            throw new DomainValidationException("invalid_return_rate", "Return rate cannot be negative.");

        var lineTotal = Money.Round2(qty * returnRate);

        return new SupplierReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReturnId = returnId,
            ProductId = productId,
            ItemName = itemName,
            Qty = qty,
            PurchasePrice = purchasePrice,
            ReturnRate = returnRate,
            LineTotal = lineTotal,
            ImeiNumbers = imeiNumbers
        };
    }

    public static SupplierReturnItem Rehydrate(
        Guid id, Guid tenantId, Guid returnId, Guid? productId, string itemName,
        int qty, decimal purchasePrice, decimal returnRate, decimal lineTotal, string[]? imeiNumbers)
    {
        return new SupplierReturnItem
        {
            Id = id,
            TenantId = tenantId,
            ReturnId = returnId,
            ProductId = productId,
            ItemName = itemName,
            Qty = qty,
            PurchasePrice = purchasePrice,
            ReturnRate = returnRate,
            LineTotal = lineTotal,
            ImeiNumbers = imeiNumbers
        };
    }

    public static SupplierReturnItem Create(
        Guid tenantId,
        Guid returnId,
        Guid? productId,
        string itemName,
        int qty,
        decimal purchasePrice,
        decimal returnRate,
        decimal lineTotal,
        string[]? imeiNumbers = null)
    {
        if (string.IsNullOrWhiteSpace(itemName))
            throw new DomainValidationException("invalid_item_name", "Item name is required.");

        if (qty <= 0)
            throw new DomainValidationException("invalid_qty", "Item quantity must be greater than zero.");

        if (purchasePrice < 0)
            throw new DomainValidationException("invalid_purchase_price", "Purchase price cannot be negative.");

        if (returnRate < 0)
            throw new DomainValidationException("invalid_return_rate", "Return rate cannot be negative.");

        return new SupplierReturnItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ReturnId = returnId,
            ProductId = productId,
            ItemName = itemName,
            Qty = qty,
            PurchasePrice = purchasePrice,
            ReturnRate = returnRate,
            LineTotal = lineTotal,
            ImeiNumbers = imeiNumbers
        };
    }
}
