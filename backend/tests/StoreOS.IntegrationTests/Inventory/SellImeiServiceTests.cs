using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Inventory;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Inventory;

[Collection("postgres")]
public class SellImeiServiceTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();
    private readonly Guid _branch2Id = Guid.NewGuid();
    private Guid _productId;
    private readonly DateOnly _purchaseDate = new(2026, 10, 1);

    public SellImeiServiceTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Sell IMEI Tenant", subdomain: "si-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch1 = Branch.Create(_tenantId, "S1", "Sell Branch 1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch1, _branch1Id);
        db.Branches.Add(branch1);

        var branch2 = Branch.Create(_tenantId, "S2", "Sell Branch 2");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch2, _branch2Id);
        db.Branches.Add(branch2);

        var product = Product.Create(_tenantId, "Sell Test Phone", ProductCategory.NewPhone, 70000m, 60000m, 18m, "85171300");
        db.Products.Add(product);
        _productId = product.Id;

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM inventory_imeis WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id IN (@b1, @b2); " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("b1", _branch1Id);
        cmd.Parameters.AddWithValue("b2", _branch2Id);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; } = Guid.NewGuid();
        public IReadOnlySet<Guid> BranchIds => new HashSet<Guid>();
        public bool AllBranches => true;
        public IReadOnlySet<string> Permissions => new HashSet<string> { "inventory:manage" };
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => true;
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    [Fact]
    public async Task LoadThenSellImei_HappyPath_UpdatesStatusToSold_WithSaleIds()
    {
        var scope = new TestAccessScope { TenantId = _tenantId };
        var interceptor = new RlsSessionInterceptor(scope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        const string testImei = "354890091234567";

        // Seed IMEI
        Guid imeiRowId;
        await using (var seedDb = new StoreOsDbContext(options, scope))
        {
            await using var tx = await seedDb.Database.BeginTransactionAsync();
            var imei = InventoryImei.Create(_tenantId, _branch1Id, _productId, null, testImei, _purchaseDate, 60000m);
            await seedDb.InventoryImeis.AddAsync(imei);
            await seedDb.SaveChangesAsync();
            await tx.CommitAsync();
            imeiRowId = imei.Id;
        }

        var saleId = Guid.NewGuid();
        var saleItemId = Guid.NewGuid();

        // Load then Sell
        await using (var db = new StoreOsDbContext(options, scope))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var service = new InventoryStockService(db, scope);

            var snapshot = await service.LoadImeiForSaleAsync(_branch1Id, testImei, CancellationToken.None);
            snapshot.Should().NotBeNull();
            snapshot.Id.Should().Be(imeiRowId);
            snapshot.ProductId.Should().Be(_productId);
            snapshot.BranchId.Should().Be(_branch1Id);
            snapshot.PurchasePrice.Should().Be(60000m);
            snapshot.Status.Should().Be(ImeiStatus.InStock);

            await service.SellImeiAsync(snapshot.Id, saleId, saleItemId, CancellationToken.None);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Verify in DB
        await using (var verifyDb = new StoreOsDbContext(options, scope))
        {
            await using var tx = await verifyDb.Database.BeginTransactionAsync();
            var row = await verifyDb.InventoryImeis.FirstOrDefaultAsync(i => i.Id == imeiRowId);
            row.Should().NotBeNull();
            row!.Status.Should().Be(ImeiStatus.Sold);
            row.SaleId.Should().Be(saleId);
            row.SaleItemId.Should().Be(saleItemId);
        }
    }

    [Fact]
    public async Task SellImei_OnAlreadySoldImei_ThrowsImeiNotAvailable()
    {
        var scope = new TestAccessScope { TenantId = _tenantId };
        var interceptor = new RlsSessionInterceptor(scope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        const string testImei = "354890091234568";

        Guid imeiRowId;
        await using (var seedDb = new StoreOsDbContext(options, scope))
        {
            await using var tx = await seedDb.Database.BeginTransactionAsync();
            var imei = InventoryImei.Create(_tenantId, _branch1Id, _productId, null, testImei, _purchaseDate, 60000m);
            imei.MarkSold(Guid.NewGuid(), Guid.NewGuid());
            await seedDb.InventoryImeis.AddAsync(imei);
            await seedDb.SaveChangesAsync();
            await tx.CommitAsync();
            imeiRowId = imei.Id;
        }

        await using (var db = new StoreOsDbContext(options, scope))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var service = new InventoryStockService(db, scope);

            var act = async () =>
            {
                await service.SellImeiAsync(imeiRowId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
                await db.SaveChangesAsync();
            };

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "imei_not_available");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task RestoreImeiToStock_OnSoldImei_Succeeds_OnNonSold_ThrowsImeiNotSold()
    {
        var scope = new TestAccessScope { TenantId = _tenantId };
        var interceptor = new RlsSessionInterceptor(scope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        const string soldImei = "354890091234569";
        const string inStockImei = "354890091234570";

        Guid soldRowId;
        Guid inStockRowId;

        await using (var seedDb = new StoreOsDbContext(options, scope))
        {
            await using var tx = await seedDb.Database.BeginTransactionAsync();
            var imei1 = InventoryImei.Create(_tenantId, _branch1Id, _productId, null, soldImei, _purchaseDate, 60000m);
            imei1.MarkSold(Guid.NewGuid(), Guid.NewGuid());
            await seedDb.InventoryImeis.AddAsync(imei1);

            var imei2 = InventoryImei.Create(_tenantId, _branch1Id, _productId, null, inStockImei, _purchaseDate, 60000m);
            await seedDb.InventoryImeis.AddAsync(imei2);

            await seedDb.SaveChangesAsync();
            await tx.CommitAsync();

            soldRowId = imei1.Id;
            inStockRowId = imei2.Id;
        }

        // 1. Restore sold IMEI -> succeeds
        await using (var db = new StoreOsDbContext(options, scope))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var service = new InventoryStockService(db, scope);

            await service.RestoreImeiToStockAsync(soldRowId, CancellationToken.None);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await using (var verifyDb = new StoreOsDbContext(options, scope))
        {
            await using var tx = await verifyDb.Database.BeginTransactionAsync();
            var row = await verifyDb.InventoryImeis.FirstOrDefaultAsync(i => i.Id == soldRowId);
            row.Should().NotBeNull();
            row!.Status.Should().Be(ImeiStatus.InStock);
            row.SaleId.Should().BeNull();
            row.SaleItemId.Should().BeNull();
        }

        // 2. Restore in-stock IMEI -> throws imei_not_sold
        await using (var db = new StoreOsDbContext(options, scope))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var service = new InventoryStockService(db, scope);

            var act = async () =>
            {
                await service.RestoreImeiToStockAsync(inStockRowId, CancellationToken.None);
                await db.SaveChangesAsync();
            };

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "imei_not_sold");

            await tx.RollbackAsync();
        }
    }

    [Fact]
    public async Task LoadImeiForSale_NonExistentOrAtDifferentBranch_ThrowsImeiNotFound()
    {
        var scope = new TestAccessScope { TenantId = _tenantId };
        var interceptor = new RlsSessionInterceptor(scope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        const string branch2Imei = "354890091234571";

        // Seed IMEI at Branch 2
        await using (var seedDb = new StoreOsDbContext(options, scope))
        {
            await using var tx = await seedDb.Database.BeginTransactionAsync();
            var imei = InventoryImei.Create(_tenantId, _branch2Id, _productId, null, branch2Imei, _purchaseDate, 60000m);
            await seedDb.InventoryImeis.AddAsync(imei);
            await seedDb.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await using (var db = new StoreOsDbContext(options, scope))
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var service = new InventoryStockService(db, scope);

            // 1. Non-existent IMEI
            var act1 = async () => await service.LoadImeiForSaleAsync(_branch1Id, "000000000000000", CancellationToken.None);
            await act1.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "imei_not_found");

            // 2. Exists at Branch 2, but queried for Branch 1
            var act2 = async () => await service.LoadImeiForSaleAsync(_branch1Id, branch2Imei, CancellationToken.None);
            await act2.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "imei_not_found");
        }
    }
}
