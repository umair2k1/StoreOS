using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Purchasing;

public class Purchase
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Qty { get; private set; }
    public decimal PurchaseRate { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierPhone { get; private set; }
    public string? SupplierAddress { get; private set; }
    public string? SellerName { get; private set; }
    public string? SellerPhone { get; private set; }
    public string? SellerAddress { get; private set; }
    public string? SupplierInvoiceNo { get; private set; }
    public Guid? ImportBatchId { get; private set; }
    public DateOnly PurchaseDate { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Purchase() { }

    public static Purchase Create(Guid tenantId, Guid branchId, Guid productId, int qty,
        decimal purchaseRate, Guid? supplierId, string? supplierName, string? supplierPhone,
        string? supplierAddress, DateOnly purchaseDate, Guid createdBy,
        string? supplierInvoiceNo = null, Guid? importBatchId = null,
        string? sellerName = null, string? sellerPhone = null, string? sellerAddress = null)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Purchase quantity must be positive.");
        if (purchaseRate < 0) throw new DomainValidationException("invalid_purchase_rate", "Purchase rate cannot be negative.");
        return new Purchase
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            Qty = qty,
            PurchaseRate = purchaseRate,
            SupplierId = supplierId,
            SupplierName = supplierName,
            SupplierPhone = supplierPhone,
            SupplierAddress = supplierAddress,
            SellerName = sellerName,
            SellerPhone = sellerPhone,
            SellerAddress = sellerAddress,
            SupplierInvoiceNo = supplierInvoiceNo,
            ImportBatchId = importBatchId,
            PurchaseDate = purchaseDate,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
