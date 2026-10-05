using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;

namespace StoreOS.Domain.Tests.Inventory;

public class InventoryImeiTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();
    private static readonly DateOnly PurchaseDate = new(2026, 10, 1);

    [Fact]
    public void Create_PositivePurchasePrice_SucceedsWithInStockStatus()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);

        Assert.Equal("123456789012345", imei.Imei);
        Assert.Equal(ImeiStatus.InStock, imei.Status);
        Assert.Equal(45000m, imei.PurchasePrice);
        Assert.Equal(PurchaseDate, imei.PurchaseDate);
        Assert.Null(imei.SaleId);
        Assert.Null(imei.SaleItemId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Create_NonPositivePurchasePrice_ThrowsImeiInvalidPurchasePrice(decimal price)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, price));

        Assert.Equal("imei_invalid_purchase_price", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyImei_ThrowsImeiRequired(string imeiStr)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            InventoryImei.Create(TenantId, BranchId, ProductId, null, imeiStr, PurchaseDate, 1000m));

        Assert.Equal("imei_required", ex.Code);
    }

    [Fact]
    public void MarkSold_FromInStock_Succeeds()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);
        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();

        imei.MarkSold(saleId, saleItemId);

        Assert.Equal(ImeiStatus.Sold, imei.Status);
        Assert.Equal(saleId, imei.SaleId);
        Assert.Equal(saleItemId, imei.SaleItemId);
    }

    [Fact]
    public void MarkSold_FromNonInStock_ThrowsImeiNotAvailable()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);
        imei.MarkSold(Guid.NewGuid(), Guid.NewGuid());

        var ex = Assert.Throws<DomainValidationException>(() =>
            imei.MarkSold(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal("imei_not_available", ex.Code);
    }

    [Fact]
    public void RestoreToStock_FromSold_Succeeds()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);
        imei.MarkSold(Guid.NewGuid(), Guid.NewGuid());

        imei.RestoreToStock();

        Assert.Equal(ImeiStatus.InStock, imei.Status);
        Assert.Null(imei.SaleId);
        Assert.Null(imei.SaleItemId);
    }

    [Fact]
    public void RestoreToStock_FromNonSold_ThrowsImeiNotSold()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);

        var ex = Assert.Throws<DomainValidationException>(() => imei.RestoreToStock());

        Assert.Equal("imei_not_sold", ex.Code);
    }

    [Fact]
    public void MarkReturnedToSupplier_FromInStock_Succeeds()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);
        var returnId = Guid.NewGuid();

        imei.MarkReturnedToSupplier(returnId);

        Assert.Equal(ImeiStatus.ReturnedToSupplier, imei.Status);
        Assert.Equal(returnId, imei.ReturnId);
    }

    [Fact]
    public void MarkReturnedToSupplier_FromNonInStock_ThrowsImeiNotAvailable()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);
        imei.MarkSold(Guid.NewGuid(), Guid.NewGuid());

        var ex = Assert.Throws<DomainValidationException>(() =>
            imei.MarkReturnedToSupplier(Guid.NewGuid()));

        Assert.Equal("imei_not_available", ex.Code);
    }

    [Fact]
    public void TransferLifecycle_MarkInTransitThenCompleteTransferIn_Succeeds()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);
        var targetBranchId = Guid.NewGuid();

        imei.MarkInTransit();
        Assert.Equal(ImeiStatus.InTransit, imei.Status);

        imei.CompleteTransferIn(targetBranchId);
        Assert.Equal(ImeiStatus.InStock, imei.Status);
        Assert.Equal(targetBranchId, imei.BranchId);
    }

    [Fact]
    public void CompleteTransferIn_WhenNotInTransit_ThrowsImeiNotInTransit()
    {
        var imei = InventoryImei.Create(TenantId, BranchId, ProductId, null, "123456789012345", PurchaseDate, 45000m);

        var ex = Assert.Throws<DomainValidationException>(() =>
            imei.CompleteTransferIn(Guid.NewGuid()));

        Assert.Equal("imei_not_in_transit", ex.Code);
    }
}
