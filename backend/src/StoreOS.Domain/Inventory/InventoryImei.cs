using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public class InventoryImei
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? PurchaseId { get; private set; }
    public string Imei { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public Guid? SaleId { get; private set; }
    public Guid? SaleItemId { get; private set; }
    public Guid? ReturnId { get; private set; }
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchasePrice { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private InventoryImei() { }

    public static InventoryImei Create(Guid tenantId, Guid branchId, Guid productId, Guid? purchaseId,
        string imei, DateOnly purchaseDate, decimal purchasePrice)
    {
        if (string.IsNullOrWhiteSpace(imei)) throw new DomainValidationException("imei_required", "IMEI is required.");
        if (purchasePrice <= 0) throw new DomainValidationException("imei_invalid_purchase_price",
            "IMEI purchase price must be a positive value at stock-in time.");
        return new InventoryImei
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            PurchaseId = purchaseId,
            Imei = imei.Trim(),
            Status = ImeiStatus.InStock,
            PurchaseDate = purchaseDate,
            PurchasePrice = purchasePrice,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static InventoryImei Rehydrate(
        Guid id, Guid tenantId, Guid branchId, Guid productId, Guid? purchaseId,
        string imei, string status, Guid? saleId, Guid? saleItemId, Guid? returnId,
        DateOnly? purchaseDate, decimal? purchasePrice, DateTimeOffset createdAt)
    {
        return new InventoryImei
        {
            Id = id,
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            PurchaseId = purchaseId,
            Imei = imei,
            Status = status,
            SaleId = saleId,
            SaleItemId = saleItemId,
            ReturnId = returnId,
            PurchaseDate = purchaseDate,
            PurchasePrice = purchasePrice,
            CreatedAt = createdAt
        };
    }

    public void MarkSold(Guid saleId, Guid saleItemId)
    {
        if (Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available", $"IMEI {Imei} is not available for sale (status: {Status}).");
        Status = ImeiStatus.Sold; SaleId = saleId; SaleItemId = saleItemId;
    }

    public void RestoreToStock()
    {
        if (Status != ImeiStatus.Sold)
            throw new DomainValidationException("imei_not_sold", $"IMEI {Imei} is not currently sold (status: {Status}).");
        Status = ImeiStatus.InStock; SaleId = null; SaleItemId = null;
    }

    public void MarkReturnedToSupplier(Guid returnId)
    {
        if (Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available",
                $"IMEI {Imei} cannot be returned to supplier from status '{Status}'.");
        Status = ImeiStatus.ReturnedToSupplier; ReturnId = returnId;
    }

    public void MarkInTransit()
    {
        if (Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available", $"IMEI {Imei} cannot be transferred from status '{Status}'.");
        Status = ImeiStatus.InTransit;
    }

    public void CompleteTransferIn(Guid newBranchId)
    {
        if (Status != ImeiStatus.InTransit)
            throw new DomainValidationException("imei_not_in_transit", $"IMEI {Imei} is not in transit.");
        Status = ImeiStatus.InStock; BranchId = newBranchId;
    }
}
