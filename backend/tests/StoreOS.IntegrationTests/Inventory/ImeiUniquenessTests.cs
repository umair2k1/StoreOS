using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Inventory;

[Collection("postgres")]
public class ImeiUniquenessTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();
    private readonly Guid _branch2Id = Guid.NewGuid();
    private Guid _productId;
    private readonly DateOnly _purchaseDate = new(2026, 10, 1);

    public ImeiUniquenessTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("IMEI Test Tenant", subdomain: "imei-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch1 = Branch.Create(_tenantId, "IB1", "Branch 1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch1, _branch1Id);
        db.Branches.Add(branch1);

        var branch2 = Branch.Create(_tenantId, "IB2", "Branch 2");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch2, _branch2Id);
        db.Branches.Add(branch2);

        var product = Product.Create(_tenantId, "IMEI Test Phone", ProductCategory.NewPhone, 60000m, 50000m, 18m, "85171300");
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
    public async Task InsertSameImei_BothInStock_SecondInsertThrowsDbUpdateException_WithPostgresUniqueViolation()
    {
        var scope = new TestAccessScope { TenantId = _tenantId };
        var interceptor = new RlsSessionInterceptor(scope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        const string duplicateImei = "990000862471854";

        // Insert first row in Branch 1
        await using (var db1 = new StoreOsDbContext(options, scope))
        {
            await using var tx1 = await db1.Database.BeginTransactionAsync();
            var imei1 = InventoryImei.Create(_tenantId, _branch1Id, _productId, null, duplicateImei, _purchaseDate, 50000m);
            await db1.InventoryImeis.AddAsync(imei1);
            await db1.SaveChangesAsync();
            await tx1.CommitAsync();
        }

        // Insert second row with same IMEI in Branch 2 (tenant-wide active uniqueness)
        await using (var db2 = new StoreOsDbContext(options, scope))
        {
            await using var tx2 = await db2.Database.BeginTransactionAsync();
            var imei2 = InventoryImei.Create(_tenantId, _branch2Id, _productId, null, duplicateImei, _purchaseDate, 50000m);
            await db2.InventoryImeis.AddAsync(imei2);

            var act = async () =>
            {
                await db2.SaveChangesAsync();
                await tx2.CommitAsync();
            };

            var ex = await act.Should().ThrowAsync<DbUpdateException>();
            ex.Which.InnerException.Should().BeOfType<PostgresException>()
                .Which.SqlState.Should().Be(PostgresErrorCodes.UniqueViolation);

            await tx2.RollbackAsync();
        }
    }

    [Fact]
    public async Task InsertSameImei_WhenFirstIsReturnedToSupplier_SecondInsertSucceeds()
    {
        var scope = new TestAccessScope { TenantId = _tenantId };
        var interceptor = new RlsSessionInterceptor(scope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        const string reusableImei = "990000862471899";

        // 1. Insert first row in Branch 1 and mark ReturnedToSupplier
        await using (var db1 = new StoreOsDbContext(options, scope))
        {
            await using var tx1 = await db1.Database.BeginTransactionAsync();
            var imei1 = InventoryImei.Create(_tenantId, _branch1Id, _productId, null, reusableImei, _purchaseDate, 50000m);
            imei1.MarkReturnedToSupplier(Guid.NewGuid());
            await db1.InventoryImeis.AddAsync(imei1);
            await db1.SaveChangesAsync();
            await tx1.CommitAsync();
        }

        // 2. Insert second row with same IMEI in Branch 2 as InStock (re-stocked)
        await using (var db2 = new StoreOsDbContext(options, scope))
        {
            await using var tx2 = await db2.Database.BeginTransactionAsync();
            var imei2 = InventoryImei.Create(_tenantId, _branch2Id, _productId, null, reusableImei, _purchaseDate, 52000m);
            await db2.InventoryImeis.AddAsync(imei2);
            await db2.SaveChangesAsync();
            await tx2.CommitAsync();

            imei2.Id.Should().NotBeEmpty();
            imei2.Status.Should().Be(ImeiStatus.InStock);
        }

        // Verify both rows exist in DB
        await using (var verifyDb = new StoreOsDbContext(options, scope))
        {
            await using var txVerify = await verifyDb.Database.BeginTransactionAsync();
            var count = await verifyDb.InventoryImeis
                .Where(x => x.TenantId == _tenantId && x.Imei == reusableImei)
                .CountAsync();
            count.Should().Be(2);
        }
    }
}
