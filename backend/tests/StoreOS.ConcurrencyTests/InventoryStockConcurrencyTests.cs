using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Inventory;
using StoreOS.Infrastructure.Persistence;
using StoreOS.IntegrationTests;

namespace StoreOS.ConcurrencyTests;

[Collection("postgres")]
public class InventoryStockConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId1 = Guid.NewGuid();
    private readonly Guid _userId2 = Guid.NewGuid();
    private Guid _productId;

    public InventoryStockConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Conc Stock Tenant", subdomain: "cstk-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "CB1", "Conc Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var product = Product.Create(_tenantId, "Conc Test Phone", ProductCategory.NewPhone, 50000m, 40000m, 18m, "85171300");
        db.Products.Add(product);
        _productId = product.Id;

        // Seed stock = 1
        var inventory = BranchInventory.Create(_tenantId, _branchId, _productId);
        inventory.IncreaseStock(1, 40000m);
        db.BranchInventories.Add(inventory);

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

    private IServiceProvider BuildServiceProvider(Guid userId)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var scope = new TestAccessScope { TenantId = _tenantId, UserId = userId };
        services.AddSingleton<IAccessScope>(scope);
        services.AddScoped<RlsSessionInterceptor>();

        services.AddDbContext<StoreOsDbContext>((sp, opts) =>
        {
            opts.UseNpgsql(_fixture.AppConnectionString)
                .AddInterceptors(sp.GetRequiredService<RlsSessionInterceptor>());
        });

        services.AddScoped<IInventoryStockService, InventoryStockService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task TwoConcurrentRequests_DecrementingStockOf1_ExactlyOneWins_AndStockNeverNegative()
    {
        var sp1 = BuildServiceProvider(_userId1);
        var sp2 = BuildServiceProvider(_userId2);

        var readyBarrier = new Barrier(2);
        var exceptions = new ConcurrentBag<Exception>();
        var successes = new ConcurrentBag<string>();

        async Task RunTask(IServiceProvider sp, string taskId)
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            var stockService = scope.ServiceProvider.GetRequiredService<IInventoryStockService>();

            // Sync start so both attempt simultaneously
            readyBarrier.SignalAndWait();

            await using var tx = await db.Database.BeginTransactionAsync();
            try
            {
                await stockService.ReserveAndDecrementStockAsync(
                    _branchId,
                    _productId,
                    qty: 1,
                    MovementType.Sale,
                    "Order",
                    $"ORD-{taskId}",
                    CancellationToken.None);

                await db.SaveChangesAsync();
                await tx.CommitAsync();
                successes.Add(taskId);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                exceptions.Add(ex);
            }
        }

        var t1 = Task.Run(() => RunTask(sp1, "T1"));
        var t2 = Task.Run(() => RunTask(sp2, "T2"));

        await Task.WhenAll(t1, t2);

        // Assert: Exactly ONE succeeded
        successes.Should().HaveCount(1, because: "only one request should win the conditional decrement");
        exceptions.Should().HaveCount(1, because: "the losing request must fail");

        var ex = exceptions.Single();
        ex.Should().Match(e => e is DomainValidationException && ((DomainValidationException)e).Code == "insufficient_stock");

        // Verify database state: stock is exactly 0, never negative, exactly one movement row
        var verifyScope = new TestAccessScope { TenantId = _tenantId, UserId = _userId1 };
        var interceptor = new RlsSessionInterceptor(verifyScope);
        var verifyOpts = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var verifyDb = new StoreOsDbContext(verifyOpts, verifyScope);
        await using var verifyTx = await verifyDb.Database.BeginTransactionAsync();
        var finalInv = await verifyDb.BranchInventories
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _productId);
        finalInv.Should().NotBeNull();
        finalInv!.StockQty.Should().Be(0, because: "stock of 1 decremented by 1 once is 0; never negative");

        var movements = await verifyDb.InventoryMovements
            .Where(m => m.TenantId == _tenantId && m.BranchId == _branchId && m.ProductId == _productId)
            .ToListAsync();

        movements.Should().HaveCount(1, because: "only the winning task should commit an InventoryMovement");
        movements[0].QtyDelta.Should().Be(-1);
    }
}
