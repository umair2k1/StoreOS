using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Tests.Catalog;

public class ProductTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_RepairService_DefaultsApplied()
    {
        var product = Product.Create(
            tenantId: TenantId,
            name: "Screen Replacement",
            category: ProductCategory.RepairService,
            sellPrice: 1500m,
            purchasePrice: 500m,
            gstRate: 0m,
            hsnCode: null);

        Assert.Equal("998713", product.HsnCode);
        Assert.Equal(18m, product.GstRate);
        Assert.Equal(60, product.EstimatedDurationMinutes);
        Assert.Equal(30, product.WarrantyDays);
    }

    [Fact]
    public void Create_NonRepairService_DefaultsNotApplied()
    {
        var product = Product.Create(
            tenantId: TenantId,
            name: "iPhone 13",
            category: ProductCategory.NewPhone,
            sellPrice: 60000m,
            purchasePrice: 50000m,
            gstRate: 18m,
            hsnCode: "85171300");

        Assert.Equal("85171300", product.HsnCode);
        Assert.Equal(18m, product.GstRate);
        Assert.Null(product.EstimatedDurationMinutes);
        Assert.Null(product.WarrantyDays);
    }

    [Fact]
    public void SetMarginScheme_True_OnAccessory_ThrowsDomainValidationException()
    {
        var product = Product.Create(
            tenantId: TenantId,
            name: "Fast Charger",
            category: ProductCategory.Accessory,
            sellPrice: 1000m,
            purchasePrice: 400m,
            gstRate: 18m,
            hsnCode: "85044030");

        var ex = Assert.Throws<DomainValidationException>(() => product.SetMarginScheme(true));
        Assert.Equal("margin_scheme_invalid_category", ex.Code);
    }

    [Fact]
    public void SetMarginScheme_True_OnUsedPhone_Succeeds()
    {
        var product = Product.Create(
            tenantId: TenantId,
            name: "Used iPhone 12",
            category: ProductCategory.UsedPhone,
            sellPrice: 30000m,
            purchasePrice: 22000m,
            gstRate: 18m,
            hsnCode: "85171300");

        product.SetMarginScheme(true);
        Assert.True(product.IsMarginScheme);
    }

    [Fact]
    public void Create_InvalidCategory_ThrowsDomainValidationException()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Product.Create(TenantId, "Unknown Item", "InvalidCategory", 100m, 50m, 18m, null));

        Assert.Equal("invalid_product_category", ex.Code);
    }

    [Theory]
    [InlineData(-10, 50, 18, "invalid_sell_price")]
    [InlineData(100, -5, 18, "invalid_purchase_price")]
    [InlineData(100, 50, -1, "invalid_gst_rate")]
    public void Create_NegativePricingOrGst_ThrowsDomainValidationException(
        decimal sellPrice, decimal purchasePrice, decimal gstRate, string expectedCode)
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Product.Create(TenantId, "Test Product", ProductCategory.Accessory, sellPrice, purchasePrice, gstRate, null));

        Assert.Equal(expectedCode, ex.Code);
    }
}
