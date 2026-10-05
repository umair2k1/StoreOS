using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Persistence;

public record ReportingProbeResult(string Isolation, string ReadOnly, int BranchCount);

public record ReportingProbeQuery(Guid TenantId) : IRequest<ReportingProbeResult>, IReportingRequest;

public class ReportingProbeHandler : IRequestHandler<ReportingProbeQuery, ReportingProbeResult>
{
    private readonly IReportingDbContext _reportingDb;

    public ReportingProbeHandler(IReportingDbContext reportingDb)
    {
        _reportingDb = reportingDb;
    }

    public async Task<ReportingProbeResult> Handle(ReportingProbeQuery request, CancellationToken cancellationToken)
    {
        var db = (ReportingDbContext)_reportingDb;
        var conn = db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

        cmd.CommandText = "SHOW transaction_isolation;";
        var isolation = (string)(await cmd.ExecuteScalarAsync(cancellationToken))!;

        cmd.CommandText = "SHOW transaction_read_only;";
        var readOnly = (string)(await cmd.ExecuteScalarAsync(cancellationToken))!;

        var count = await _reportingDb.Set<Branch>().CountAsync(cancellationToken);
        return new ReportingProbeResult(isolation, readOnly, count);
    }
}

[Collection("postgres")]
public class ReportingDbContextTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();

    public ReportingDbContextTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        var subA = "rep-a-" + _tenantAId.ToString("N")[..8];
        var subB = "rep-b-" + _tenantBId.ToString("N")[..8];

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
            "VALUES (@tA, 'Rep Tenant A', @subA, true, 'Asia/Kolkata', '{}'::jsonb, now()), " +
            "       (@tB, 'Rep Tenant B', @subB, true, 'Asia/Kolkata', '{}'::jsonb, now());", conn);
        cmd.Parameters.AddWithValue("tA", _tenantAId);
        cmd.Parameters.AddWithValue("subA", subA);
        cmd.Parameters.AddWithValue("tB", _tenantBId);
        cmd.Parameters.AddWithValue("subB", subB);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM branches WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM tenants WHERE id IN (@tA, @tB);", conn);
        cmd.Parameters.AddWithValue("tA", _tenantAId);
        cmd.Parameters.AddWithValue("tB", _tenantBId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public TestAccessScope(Guid tenantId)
        {
            TenantId = tenantId;
            UserId = Guid.NewGuid();
            BranchIds = new HashSet<Guid>();
            Permissions = new HashSet<string>();
        }

        public Guid TenantId { get; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; }
        public IReadOnlySet<Guid> BranchIds { get; }
        public bool AllBranches => true;
        public IReadOnlySet<string> Permissions { get; }
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => true;
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) => throw new NotSupportedException();
        public void UseSystemContext(Guid tenantId) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ReportingDbContext_ReadsPersistedData_WithNoTracking_AndEnforcesTenantIsolation()
    {
        var scopeA = new TestAccessScope(_tenantAId);

        // 1. Seed Branch row via StoreOsDbContext (write side)
        var writeOptions = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using (var writeDb = new StoreOsDbContext(writeOptions, scopeA))
        {
            var branch = Branch.Create(_tenantAId, "R01", "Reporting Branch A");
            await writeDb.Branches.AddAsync(branch);
            await writeDb.SaveChangesAsync();
        }

        // 2. Read back via ReportingDbContext scoped to same tenant A
        var readOptions = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scopeA))
            .Options;

        await using (var reportingDbA = new ReportingDbContext(readOptions, scopeA))
        {
            await using var txA = await reportingDbA.Database.BeginTransactionAsync();
            var branches = await reportingDbA.Set<Branch>().ToListAsync();
            await txA.CommitAsync();

            branches.Should().ContainSingle(b => b.Code == "R01" && b.Name == "Reporting Branch A");
            reportingDbA.ChangeTracker.Entries().Should().BeEmpty("ReportingDbContext must use QueryTrackingBehavior.NoTracking globally");
        }

        // 3. Confirm Tenant B cannot see Tenant A's branch
        var scopeB = new TestAccessScope(_tenantBId);
        var readOptionsB = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scopeB))
            .Options;

        await using (var reportingDbB = new ReportingDbContext(readOptionsB, scopeB))
        {
            await using var txB = await reportingDbB.Database.BeginTransactionAsync();
            var branchesB = await reportingDbB.Set<Branch>().ToListAsync();
            await txB.CommitAsync();

            branchesB.Should().BeEmpty("ReportingDbContext must enforce tenant isolation identical to StoreOsDbContext");
            reportingDbB.ChangeTracker.Entries().Should().BeEmpty();
        }
    }

    [Fact]
    public async Task IReportingRequest_Through_TransactionBehavior_Runs_On_ReportingDbContext_With_RepeatableRead_ReadOnly()
    {
        var scopeA = new TestAccessScope(_tenantAId);

        // Seed branch
        var writeOptions = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using (var writeDb = new StoreOsDbContext(writeOptions, scopeA))
        {
            var branch = Branch.Create(_tenantAId, "R02", "Reporting Branch Probe");
            await writeDb.Branches.AddAsync(branch);
            await writeDb.SaveChangesAsync();
        }

        // Configure DI with TransactionBehavior and both DbContexts
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAccessScope>(scopeA);

        var interceptor = new RlsSessionInterceptor(scopeA);
        services.AddDbContext<StoreOsDbContext>(opt =>
        {
            opt.UseNpgsql(_fixture.AppConnectionString);
            opt.AddInterceptors(interceptor);
        });
        services.AddDbContext<ReportingDbContext>(opt =>
        {
            opt.UseNpgsql(_fixture.AppConnectionString);
            opt.AddInterceptors(interceptor);
        });
        services.AddScoped<IReportingDbContext>(sp => sp.GetRequiredService<ReportingDbContext>());

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ReportingProbeHandler).Assembly);
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var result = await mediator.Send(new ReportingProbeQuery(_tenantAId));

        result.Isolation.ToLowerInvariant().Should().Be("repeatable read", because: "IReportingRequest must run under RepeatableRead snapshot isolation");
        result.ReadOnly.ToLowerInvariant().Should().Be("on", because: "IReportingRequest must set transaction_read_only = on");
        result.BranchCount.Should().BeGreaterThanOrEqualTo(1);
    }
}
