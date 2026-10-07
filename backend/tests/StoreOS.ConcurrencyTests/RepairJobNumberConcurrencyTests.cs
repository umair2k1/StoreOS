using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Sales;
using StoreOS.IntegrationTests;

namespace StoreOS.ConcurrencyTests;

[Collection("postgres")]
public class RepairJobNumberConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private const string BranchCode = "B01";

    public RepairJobNumberConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Repair Job Concurrency Tenant", subdomain: "rjc-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Repair Branch 01");
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
            DELETE FROM repair_jobs WHERE tenant_id = @t;
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
    public async Task TwentyConcurrentJobIntakes_ShouldProduceUniqueSequentialJobNumbers()
    {
        const int concurrentTasks = 20;
        var date = new DateOnly(2026, 10, 7);
        var seriesKey = $"REP-{BranchCode}-{date:yyyyMMdd}";
        var jobNumbers = new ConcurrentBag<string>();

        var tasks = Enumerable.Range(0, concurrentTasks).Select(async i =>
        {
            await using var db = CreateAppDbContext();
            await using var tx = await db.Database.BeginTransactionAsync();

            var allocator = new InvoiceSequenceAllocator(db);
            var seq = await allocator.AllocateNextAsync(_tenantId, _branchId, seriesKey, CancellationToken.None);
            var jobNumber = RepairJobNumberFormatter.Format(BranchCode, date, seq);

            var job = RepairJob.Intake(
                _tenantId,
                _branchId,
                jobNumber,
                customerId: null,
                customerName: $"Customer {i}",
                customerPhone: null,
                customerAddress: null,
                deviceBrand: "Brand",
                deviceModel: "Model",
                deviceType: "Phone",
                serialNumber: null,
                imei1: null,
                imei2: null,
                deviceCondition: null,
                accessoriesReceived: null,
                problemDescription: $"Issue {i}",
                customerNotes: null);

            await db.RepairJobs.AddAsync(job);
            await db.SaveChangesAsync();

            jobNumbers.Add(jobNumber);
            await tx.CommitAsync();
        });

        await Task.WhenAll(tasks);

        var sortedNumbers = jobNumbers.OrderBy(x => x).ToList();
        sortedNumbers.Should().HaveCount(concurrentTasks);
        sortedNumbers.Distinct().Should().HaveCount(concurrentTasks);

        var expectedNumbers = Enumerable.Range(1, concurrentTasks)
            .Select(seq => RepairJobNumberFormatter.Format(BranchCode, date, seq))
            .ToList();

        sortedNumbers.Should().Equal(expectedNumbers);
    }
}
