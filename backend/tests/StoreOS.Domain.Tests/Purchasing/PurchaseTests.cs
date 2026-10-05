using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Purchasing;

namespace StoreOS.Domain.Tests.Purchasing;

public class PurchaseTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();
    private static readonly DateOnly PurchaseDate = new(2026, 10, 5);

    [Fact]
    public void Create_ValidWithOptionalFieldsNull_Succeeds()
    {
        var purchase = Purchase.Create(
            tenantId: TenantId,
            branchId: BranchId,
            productId: ProductId,
            qty: 10,
            purchaseRate: 1500m,
            supplierId: null,
            supplierName: null,
            supplierPhone: null,
            supplierAddress: null,
            purchaseDate: PurchaseDate,
            createdBy: CreatedBy);

        Assert.Equal(TenantId, purchase.TenantId);
        Assert.Equal(BranchId, purchase.BranchId);
        Assert.Equal(ProductId, purchase.ProductId);
        Assert.Equal(10, purchase.Qty);
        Assert.Equal(1500m, purchase.PurchaseRate);
        Assert.Equal(PurchaseDate, purchase.PurchaseDate);
        Assert.Equal(CreatedBy, purchase.CreatedBy);
        Assert.Null(purchase.SupplierId);
        Assert.Null(purchase.SupplierName);
        Assert.Null(purchase.SupplierInvoiceNo);
        Assert.Null(purchase.SellerName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_ZeroOrNegativeQty_ThrowsInvalidQuantity(int qty)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Purchase.Create(
                TenantId, BranchId, ProductId, qty, 1000m, null, null, null, null, PurchaseDate, CreatedBy));

        Assert.Equal("invalid_quantity", ex.Code);
    }

    [Fact]
    public void Create_NegativePurchaseRate_ThrowsInvalidPurchaseRate()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Purchase.Create(
                TenantId, BranchId, ProductId, 5, -10m, null, null, null, null, PurchaseDate, CreatedBy));

        Assert.Equal("invalid_purchase_rate", ex.Code);
    }
}
