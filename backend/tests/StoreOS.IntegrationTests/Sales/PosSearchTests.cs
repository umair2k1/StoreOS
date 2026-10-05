using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Search;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Security;
using StoreOS.IntegrationTests;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class PosSearchTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Guid _phoneId;
    private Guid _accessoryId;
    private Guid _serviceId;
    private const string TestImei = "860123456789012";

    public PosSearchTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("POS Test Tenant", subdomain: "pos-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "BA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "BB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        // 1. Phone product
        var phone = Product.Create(_tenantId, "Pixel 8 Pro", ProductCategory.NewPhone, 75000m, 60000m, 18m, "85171300");
        db.Products.Add(phone);
        _phoneId = phone.Id;

        // 2. Accessory product
        var accessory = Product.Create(_tenantId, "Screen Guard Glass", ProductCategory.Accessory, 499m, 150m, 18m, "39269099");
        db.Products.Add(accessory);
        _accessoryId = accessory.Id;

        // 3. Repair service product
        var service = Product.Create(_tenantId, "Screen Replacement Service", ProductCategory.RepairService, 2500m, 0m, 18m, "998713");
        db.Products.Add(service);
        _serviceId = service.Id;

        // Stock in Branch A
        var invPhoneA = BranchInventory.Create(_tenantId, _branchAId, _phoneId);
        invPhoneA.IncreaseStock(5, 60000m);
        db.BranchInventories.Add(invPhoneA);

        var invAccA = BranchInventory.Create(_tenantId, _branchAId, _accessoryId);
        invAccA.IncreaseStock(2, 150m); // below threshold 3 -> LowStock
        db.BranchInventories.Add(invAccA);

        // Stock in Branch B (Phone has 10 at B)
        var invPhoneB = BranchInventory.Create(_tenantId, _branchBId, _phoneId);
        invPhoneB.IncreaseStock(10, 60000m);
        db.BranchInventories.Add(invPhoneB);

        // IMEIs at Branch A: 1 InStock, 1 Sold
        var imei1 = InventoryImei.Create(
            _tenantId, _branchAId, _phoneId, purchaseId: null,
            imei: TestImei, purchaseDate: DateOnly.FromDateTime(DateTime.UtcNow), purchasePrice: 60000m);
        db.InventoryImeis.Add(imei1);

        var imeiSold = InventoryImei.Create(
            _tenantId, _branchAId, _phoneId, purchaseId: null,
            imei: "860999999999999", purchaseDate: DateOnly.FromDateTime(DateTime.UtcNow), purchasePrice: 60000m);
        imeiSold.MarkSold(Guid.NewGuid(), Guid.NewGuid());
        db.InventoryImeis.Add(imeiSold);
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
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    private async Task<StoreOsDbContext> CreateAppDbContextAsync(Guid branchId)
    {
        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            UserId = _userId,
            BranchIds = new HashSet<Guid> { branchId }
        };
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope))
            .Options;
        var db = new StoreOsDbContext(options, scope);
        await db.Database.BeginTransactionAsync();
        return db;
    }

    [Fact]
    public async Task NameSearch_ReturnsMatchingProductsWithBranchScopedStock()
    {
        await using var db = await CreateAppDbContextAsync(_branchAId);
        var branchContext = new MutableBranchContext { CurrentBranchId = _branchAId };

        var handler = new PosSearchHandler(db, branchContext);
        var results = await handler.Handle(new PosSearchQuery("Pixel"), CancellationToken.None);

        results.Should().HaveCount(1);
        var hit = results[0];
        hit.ProductId.Should().Be(_phoneId);
        hit.Name.Should().Be("Pixel 8 Pro");
        hit.StockQty.Should().Be(5);
        hit.StockStatus.Should().Be("InStock");
    }

    [Fact]
    public async Task ExactImeiSearch_ReturnsProductOwningThatImei()
    {
        await using var db = await CreateAppDbContextAsync(_branchAId);
        var branchContext = new MutableBranchContext { CurrentBranchId = _branchAId };

        var handler = new PosSearchHandler(db, branchContext);
        var results = await handler.Handle(new PosSearchQuery(TestImei), CancellationToken.None);

        results.Should().NotBeEmpty();
        results.First().ProductId.Should().Be(_phoneId);
    }

    [Fact]
    public async Task RepairService_AppearsInSearchWithNaStockStatus()
    {
        await using var db = await CreateAppDbContextAsync(_branchAId);
        var branchContext = new MutableBranchContext { CurrentBranchId = _branchAId };

        var handler = new PosSearchHandler(db, branchContext);
        var results = await handler.Handle(new PosSearchQuery("Replacement"), CancellationToken.None);

        results.Should().HaveCount(1);
        var hit = results[0];
        hit.ProductId.Should().Be(_serviceId);
        hit.Category.Should().Be(ProductCategory.RepairService);
        hit.StockQty.Should().Be(0);
        hit.StockStatus.Should().Be("N/A");
    }

    [Fact]
    public async Task GetPosProduct_ListsExactlyInStockImeisAtBranch()
    {
        await using var db = await CreateAppDbContextAsync(_branchAId);
        var branchContext = new MutableBranchContext { CurrentBranchId = _branchAId };

        var handler = new GetPosProductHandler(db, branchContext);
        var result = await handler.Handle(new GetPosProductQuery(_phoneId), CancellationToken.None);

        result.ProductId.Should().Be(_phoneId);
        result.StockQty.Should().Be(5);
        result.StockStatus.Should().Be("InStock");
        result.AvailableImeis.Should().Equal(TestImei);
        result.AvailableImeis.Should().NotContain("860999999999999"); // Sold IMEI is excluded
    }

    [Fact]
    public async Task UserScopedToBranchA_DoesNotSeeBranchBStock()
    {
        // Branch A has stock 5 for Pixel
        await using var dbA = await CreateAppDbContextAsync(_branchAId);
        var branchContextA = new MutableBranchContext { CurrentBranchId = _branchAId };

        var handlerA = new PosSearchHandler(dbA, branchContextA);
        var resultsA = await handlerA.Handle(new PosSearchQuery("Pixel"), CancellationToken.None);
        resultsA[0].StockQty.Should().Be(5);

        // Branch B has stock 10 for Pixel
        await using var dbB = await CreateAppDbContextAsync(_branchBId);
        var branchContextB = new MutableBranchContext { CurrentBranchId = _branchBId };

        var handlerB = new PosSearchHandler(dbB, branchContextB);
        var resultsB = await handlerB.Handle(new PosSearchQuery("Pixel"), CancellationToken.None);
        resultsB[0].StockQty.Should().Be(10);
    }
}
