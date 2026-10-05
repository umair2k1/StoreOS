using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Sales;
using StoreOS.IntegrationTests;

namespace StoreOS.ConcurrencyTests;

[Collection("postgres")]
public class InvoiceSequenceConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    public InvoiceSequenceConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Seq Tenant", subdomain: "seq-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "SB1", "Seq Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM invoice_sequences WHERE tenant_id = @t;
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
        public bool AllBranches => true;
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => true;
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    private StoreOsDbContext CreateAppDbContext()
    {
        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            BranchIds = new HashSet<Guid> { _branchId }
        };
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope))
            .Options;
        return new StoreOsDbContext(options, scope);
    }

    [Fact]
    public async Task FiftyConcurrentAllocations_ShouldProduceExactConsecutiveSequenceWithNoGapsOrDuplicates()
    {
        const int concurrentTasks = 50;
        var seriesKey = "FY26-27";
        var results = new ConcurrentBag<int>();

        var tasks = Enumerable.Range(0, concurrentTasks).Select(async _ =>
        {
            await using var db = CreateAppDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var allocator = new InvoiceSequenceAllocator(db);

            var seq = await allocator.AllocateNextAsync(_tenantId, _branchId, seriesKey, CancellationToken.None);
            results.Add(seq);

            await tx.CommitAsync();
        });

        await Task.WhenAll(tasks);

        var sortedResults = results.OrderBy(x => x).ToList();
        sortedResults.Should().HaveCount(concurrentTasks);
        sortedResults.Should().Equal(Enumerable.Range(1, concurrentTasks));
    }

    [Fact]
    public async Task RolledBackAllocation_ShouldNotWasteSequenceNumber()
    {
        var seriesKey = "ROLLBACK-TEST-FY26-27";

        // Transaction 1: Allocate then rollback
        await using (var db1 = CreateAppDbContext())
        {
            await using var tx1 = await db1.Database.BeginTransactionAsync();
            var allocator1 = new InvoiceSequenceAllocator(db1);

            var firstSeq = await allocator1.AllocateNextAsync(_tenantId, _branchId, seriesKey, CancellationToken.None);
            firstSeq.Should().Be(1);

            await tx1.RollbackAsync();
        }

        // Transaction 2: Allocate again in fresh transaction
        await using (var db2 = CreateAppDbContext())
        {
            await using var tx2 = await db2.Database.BeginTransactionAsync();
            var allocator2 = new InvoiceSequenceAllocator(db2);

            var secondSeq = await allocator2.AllocateNextAsync(_tenantId, _branchId, seriesKey, CancellationToken.None);
            await tx2.CommitAsync();

            // Postgres row-level UPSERT changes participate in ambient transactions;
            // rolling back transaction 1 rolls back the row insertion/update, so second allocation returns 1.
            secondSeq.Should().Be(1);
        }
    }
}
