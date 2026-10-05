using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Inventory;
using StoreOS.Infrastructure.Persistence;
using StoreOS.IntegrationTests;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class PricingResolverTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Guid _newPhoneId;
    private Guid _usedPhoneId;
    private Guid _accessoryId;
    private Guid _inactivePhoneId;
    private const string PhoneImei = "860111111111111";

    public PricingResolverTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Pricing Test Tenant", subdomain: "prc-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "PB1", "Pricing Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // 1. New Phone: product sell price 50000, branch override 52000
        var newPhone = Product.Create(_tenantId, "Galaxy S24", ProductCategory.NewPhone, 50000m, 40000m, 18m, "85171300");
        db.Products.Add(newPhone);
        _newPhoneId = newPhone.Id;

        var invNewPhone = BranchInventory.Create(_tenantId, _branchId, _newPhoneId);
        invNewPhone.SetSellPriceOverride(52000m);
        invNewPhone.IncreaseStock(10, 40000m);
        db.BranchInventories.Add(invNewPhone);

        // 2. Used Phone: sell price 30000, product purchase price 20000, branch inventory cost 22000
        var usedPhone = Product.Create(_tenantId, "iPhone 12 Used", ProductCategory.UsedPhone, 30000m, 20000m, 18m, "85171200");
        db.Products.Add(usedPhone);
        _usedPhoneId = usedPhone.Id;

        var invUsedPhone = BranchInventory.Create(_tenantId, _branchId, _usedPhoneId);
        invUsedPhone.IncreaseStock(5, 22000m); // cost price 22000
        db.BranchInventories.Add(invUsedPhone);

        // IMEI for Used Phone with purchase price 25000 (different from branch cost 22000)
        var usedImei = InventoryImei.Create(
            _tenantId, _branchId, _usedPhoneId, purchaseId: null,
            imei: PhoneImei, purchaseDate: DateOnly.FromDateTime(DateTime.UtcNow), purchasePrice: 25000m);
        db.InventoryImeis.Add(usedImei);

        // 3. Accessory: sell price 999, margin scheme invalid
        var accessory = Product.Create(_tenantId, "Charger 65W", ProductCategory.Accessory, 999m, 400m, 18m, "85044090");
        db.Products.Add(accessory);
        _accessoryId = accessory.Id;

        var invAcc = BranchInventory.Create(_tenantId, _branchId, _accessoryId);
        invAcc.IncreaseStock(20, 400m);
        db.BranchInventories.Add(invAcc);

        // 4. Inactive Phone
        var inactivePhone = Product.Create(_tenantId, "Old Inactive Phone", ProductCategory.NewPhone, 15000m, 10000m, 18m, "85171300");
        inactivePhone.Deactivate();
        db.Products.Add(inactivePhone);
        _inactivePhoneId = inactivePhone.Id;

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM inventory_imeis WHERE tenant_id = @t;
            DELETE FROM branch_inventory WHERE tenant_id = @t;
            DELETE FROM products WHERE tenant_id = @t;
            DELETE FROM branches WHERE tenant_id = @t;
            DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; } = Guid.NewGuid();
        public IReadOnlySet<Guid> BranchIds { get; init; } = new HashSet<Guid>();
        public bool AllBranches => false;
        public IReadOnlySet<string> Permissions => new HashSet<string> { "inventory:manage" };
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    private async Task<(StoreOsDbContext db, PricingResolver resolver, TestAccessScope scope)> CreateContextAsync()
    {
        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            UserId = _userId,
            BranchIds = new HashSet<Guid> { _branchId }
        };
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope))
            .Options;
        var db = new StoreOsDbContext(options, scope);
        await db.Database.BeginTransactionAsync();

        var stockService = new InventoryStockService(db, scope);
        var resolver = new PricingResolver(db, scope, stockService);
        return (db, resolver, scope);
    }

    [Fact]
    public async Task Resolve_StandardNewPhone_UsesBranchOverridePriceAndStandardGst()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var line = new CheckoutCartLine(_newPhoneId, 1, null, false, false, null);
            var resolved = await resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            resolved.PricePerUnit.Should().Be(52000m); // Branch override
            resolved.IsMarginScheme.Should().BeFalse();
            resolved.LineTaxable.Should().Be(44067.80m);
            resolved.LineCgst.Should().Be(3966.10m);
            resolved.LineSgst.Should().Be(3966.10m);
            (resolved.LineTaxable + resolved.LineCgst + resolved.LineSgst).Should().Be(52000m);
        }
    }

    [Fact]
    public async Task Resolve_UsedPhoneWithImeiAndMarginScheme_ImeiPurchasePriceWinsAsCostBasis()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var line = new CheckoutCartLine(_usedPhoneId, 1, PhoneImei, true, false, null);
            var resolved = await resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            resolved.PricePerUnit.Should().Be(30000m);
            resolved.PurchasePriceSnapshot.Should().Be(25000m); // IMEI cost 25000 wins over branch cost 22000
            resolved.IsMarginScheme.Should().BeTrue();

            // Margin = 30000 - 25000 = 5000; Tax on 5000 with 18%: Taxable = 4237.29, CGST = 381.36, SGST = 381.35
            resolved.LineTaxable.Should().Be(4237.29m);
            resolved.LineCgst.Should().Be(381.36m);
            resolved.LineSgst.Should().Be(381.35m);
        }
    }

    [Fact]
    public async Task Resolve_UsedPhoneWithoutImei_UsesBranchInventoryCostPrice()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var line = new CheckoutCartLine(_usedPhoneId, 1, null, true, false, null);
            var resolved = await resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            resolved.PurchasePriceSnapshot.Should().Be(22000m); // branch_inventory.cost_price
            resolved.IsMarginScheme.Should().BeTrue();
            // Margin = 30000 - 22000 = 8000; 18% GST on 8000: Taxable = 6779.66, CGST = 610.17, SGST = 610.17
            resolved.LineTaxable.Should().Be(6779.66m);
            resolved.LineCgst.Should().Be(610.17m);
            resolved.LineSgst.Should().Be(610.17m);
        }
    }

    [Fact]
    public async Task Resolve_UsedPhoneWithoutImeiAndZeroBranchCost_FallsBackToProductPurchasePrice()
    {
        // Create used phone with zero branch cost
        var (db, resolver, scope) = await CreateContextAsync();
        await using (db)
        {
            var fallbackPhone = Product.Create(_tenantId, "Used Phone Fallback", ProductCategory.UsedPhone, 20000m, 15000m, 18m, "85171200");
            db.Products.Add(fallbackPhone);
            var inv = BranchInventory.Create(_tenantId, _branchId, fallbackPhone.Id); // CostPrice = 0
            db.BranchInventories.Add(inv);
            await db.SaveChangesAsync();

            var line = new CheckoutCartLine(fallbackPhone.Id, 1, null, true, false, null);
            var resolved = await resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            resolved.PurchasePriceSnapshot.Should().Be(15000m); // Product purchase price
            resolved.IsMarginScheme.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Resolve_MarginSchemeOnAccessory_ThrowsMarginSchemeInvalidCategory()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var line = new CheckoutCartLine(_accessoryId, 1, null, true, false, null);
            var act = () => resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "margin_scheme_invalid_category");
        }
    }

    [Fact]
    public async Task Resolve_MarginSchemeWithZeroCost_ThrowsMarginSchemeInvalidCostBasis()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var zeroCostPhone = Product.Create(_tenantId, "Zero Cost Used", ProductCategory.UsedPhone, 20000m, 0m, 18m, "85171200");
            db.Products.Add(zeroCostPhone);
            var inv = BranchInventory.Create(_tenantId, _branchId, zeroCostPhone.Id); // Cost = 0
            db.BranchInventories.Add(inv);
            await db.SaveChangesAsync();

            var line = new CheckoutCartLine(zeroCostPhone.Id, 1, null, true, false, null);
            var act = () => resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "margin_scheme_invalid_cost_basis");
        }
    }

    [Fact]
    public async Task Resolve_GiftLine_LineTaxIsZero()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var line = new CheckoutCartLine(_accessoryId, 1, null, false, true, null);
            var resolved = await resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            resolved.IsGift.Should().BeTrue();
            resolved.PricePerUnit.Should().Be(999m);
            resolved.LineTaxable.Should().Be(0m);
            resolved.LineCgst.Should().Be(0m);
            resolved.LineSgst.Should().Be(0m);
        }
    }

    [Fact]
    public async Task Resolve_ImeiBelongingToDifferentProduct_ThrowsImeiProductMismatch()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            // PhoneImei belongs to _usedPhoneId, but line requests _newPhoneId
            var line = new CheckoutCartLine(_newPhoneId, 1, PhoneImei, false, false, null);
            var act = () => resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "imei_product_mismatch");
        }
    }

    [Fact]
    public async Task Resolve_InactiveProduct_ThrowsProductInactive()
    {
        var (db, resolver, _) = await CreateContextAsync();
        await using (db)
        {
            var line = new CheckoutCartLine(_inactivePhoneId, 1, null, false, false, null);
            var act = () => resolver.ResolveAsync(_branchId, line, CancellationToken.None);

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "product_inactive");
        }
    }
}
