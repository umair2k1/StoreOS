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
public class ImeiDoubleSaleConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId1 = Guid.NewGuid();
    private readonly Guid _userId2 = Guid.NewGuid();
    private Guid _productId;
    private const string TargetImei = "359999081234567";

    public ImeiDoubleSaleConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Imei Conc Tenant", subdomain: "ic-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "ICB", "Imei Conc Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var product = Product.Create(_tenantId, "Imei Conc Phone", ProductCategory.NewPhone, 80000m, 70000m, 18m, "85171300");
        db.Products.Add(product);
        _productId = product.Id;

        // Seed 1 InStock IMEI
        var imei = InventoryImei.Create(_tenantId, _branchId, _productId, null, TargetImei, new DateOnly(2026, 10, 1), 70000m);
        db.InventoryImeis.Add(imei);

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
    public async Task TwoConcurrentRequests_SellingSameImei_ExactlyOneWins()
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

            readyBarrier.SignalAndWait();

            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
            try
            {
                var snapshot = await stockService.LoadImeiForSaleAsync(_branchId, TargetImei, CancellationToken.None);
                await stockService.SellImeiAsync(snapshot.Id, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
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

        // Assert: Exactly ONE succeeded, exactly ONE failed
        successes.Should().HaveCount(1, because: "only one request can sell the physical IMEI");
        exceptions.Should().HaveCount(1, because: "the other request must fail");

        var failure = exceptions.Single();

        // Check observed failure mode:
        // Empirically: The FOR UPDATE lock forces the 2nd transaction to block until the 1st commits.
        // Once unblocked, the 2nd transaction reads status == "Sold", so MarkSold throws DomainValidationException("imei_not_available").
        // Or if concurrent commit occurs, Postgres unique constraint / serialization could throw.
        failure.Should().Match(ex =>
            (ex is DomainValidationException && ((DomainValidationException)ex).Code == "imei_not_available") ||
            ex is DbUpdateException ||
            (ex is PostgresException && ((PostgresException)ex).SqlState == PostgresErrorCodes.SerializationFailure) ||
            (ex is Exception && ((Exception)ex).InnerException is PostgresException && ((PostgresException)((Exception)ex).InnerException!).SqlState == PostgresErrorCodes.SerializationFailure));

        // Verify final state in database: status is Sold
        var verifyScope = new TestAccessScope { TenantId = _tenantId, UserId = _userId1 };
        var interceptor = new RlsSessionInterceptor(verifyScope);
        var verifyOpts = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        await using var verifyDb = new StoreOsDbContext(verifyOpts, verifyScope);
        await using var verifyTx = await verifyDb.Database.BeginTransactionAsync();
        var row = await verifyDb.InventoryImeis.FirstOrDefaultAsync(i => i.TenantId == _tenantId && i.Imei == TargetImei);
        row.Should().NotBeNull();
        row!.Status.Should().Be(ImeiStatus.Sold);
        row.SaleId.Should().NotBeNull();
    }
}
