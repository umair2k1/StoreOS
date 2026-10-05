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
public class InventoryStockServiceTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private Guid _productId;

    public InventoryStockServiceTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Stock Test Tenant", subdomain: "stk-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "SB1", "Stock Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var product = Product.Create(_tenantId, "Stock Test Item", ProductCategory.Accessory, 500m, 200m, 18m, "85444299");
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
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; }
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
    public async Task IncreaseStock_ThenDecreaseStock_PersistsCorrectInventoryAndMovements()
    {
        var scope = new TestAccessScope { TenantId = _tenantId, UserId = _userId };
        var interceptor = new RlsSessionInterceptor(scope);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new StoreOsDbContext(options, scope);
        await using var tx = await db.Database.BeginTransactionAsync();

        var service = new InventoryStockService(db, scope);

        // 1. Increase stock by 10 at unit cost 200
        await service.IncreaseStockAsync(_branchId, _productId, 10, 200m, MovementType.Purchase, "Invoice", "INV-100", CancellationToken.None);
        await db.SaveChangesAsync();

        // Check intermediate state
        var invAfterIncrease = await db.BranchInventories
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _productId);
        invAfterIncrease.Should().NotBeNull();
        invAfterIncrease!.StockQty.Should().Be(10);
        invAfterIncrease.CostPrice.Should().Be(200m);

        // 2. Decrement stock by 4
        await service.ReserveAndDecrementStockAsync(_branchId, _productId, 4, MovementType.Sale, "Order", "ORD-200", CancellationToken.None);
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        // 3. Verify final state
        await using var verifyDb = new StoreOsDbContext(options, scope);
        await using var verifyTx = await verifyDb.Database.BeginTransactionAsync();
        var finalInv = await verifyDb.BranchInventories
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _productId);
        finalInv.Should().NotBeNull();
        finalInv!.StockQty.Should().Be(6);

        var movements = await verifyDb.InventoryMovements
            .Where(m => m.TenantId == _tenantId && m.BranchId == _branchId && m.ProductId == _productId)
            .OrderBy(m => m.Id)
            .ToListAsync();
        movements.Should().HaveCount(2);

        movements[0].MovementType.Should().Be(MovementType.Purchase);
        movements[0].QtyDelta.Should().Be(10);
        movements[0].UnitCost.Should().Be(200m);
        movements[0].RefType.Should().Be("Invoice");
        movements[0].RefId.Should().Be("INV-100");
        movements[0].CreatedBy.Should().Be(_userId);

        movements[1].MovementType.Should().Be(MovementType.Sale);
        movements[1].QtyDelta.Should().Be(-4);
        movements[1].UnitCost.Should().Be(200m);
        movements[1].RefType.Should().Be("Order");
        movements[1].RefId.Should().Be("ORD-200");
        movements[1].CreatedBy.Should().Be(_userId);
    }

    [Fact]
    public async Task ReserveAndDecrementStock_BelowZero_ThrowsInsufficientStock_AndRollsBackWithoutMovements()
    {
        var scope = new TestAccessScope { TenantId = _tenantId, UserId = _userId };
        var interceptor = new RlsSessionInterceptor(scope);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        // Seed stock = 2
        await using (var seedDb = new StoreOsDbContext(options, scope))
        {
            await using var seedTx = await seedDb.Database.BeginTransactionAsync();
            var service = new InventoryStockService(seedDb, scope);
            await service.IncreaseStockAsync(_branchId, _productId, 2, 150m, MovementType.Purchase, "Invoice", "INV-INIT", CancellationToken.None);
            await seedDb.SaveChangesAsync();
            await seedTx.CommitAsync();
        }

        // Try to decrement by 5 in a new transaction
        await using (var failDb = new StoreOsDbContext(options, scope))
        {
            await using var failTx = await failDb.Database.BeginTransactionAsync();
            var service = new InventoryStockService(failDb, scope);

            var act = async () =>
            {
                await service.ReserveAndDecrementStockAsync(_branchId, _productId, 5, MovementType.Sale, "Order", "ORD-FAIL", CancellationToken.None);
                await failDb.SaveChangesAsync();
                await failTx.CommitAsync();
            };

            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(ex => ex.Code == "insufficient_stock");

            await failTx.RollbackAsync();
        }

        // Verify stock remains 2 and no ORD-FAIL movement was persisted
        await using var verifyDb = new StoreOsDbContext(options, scope);
        await using var verifyTx = await verifyDb.Database.BeginTransactionAsync();
        var inv = await verifyDb.BranchInventories
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _productId);
        inv.Should().NotBeNull();
        inv!.StockQty.Should().Be(2);

        var failMovement = await verifyDb.InventoryMovements
            .FirstOrDefaultAsync(m => m.TenantId == _tenantId && m.RefId == "ORD-FAIL");
        failMovement.Should().BeNull();
    }
}
