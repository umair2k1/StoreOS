using System.Data;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Persistence;

#region Test Probes & Handlers

public record CountBranchesProbe : IRequest<int>, IReadOnlyRequest;

public class CountBranchesProbeHandler : IRequestHandler<CountBranchesProbe, int>
{
    private readonly StoreOsDbContext _db;
    public CountBranchesProbeHandler(StoreOsDbContext db) => _db = db;
    public Task<int> Handle(CountBranchesProbe request, CancellationToken ct) => _db.Branches.CountAsync(ct);
}

public record InsertBranchProbe(string Code, string Name) : IRequest<Guid>;

public class InsertBranchProbeHandler : IRequestHandler<InsertBranchProbe, Guid>
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    public InsertBranchProbeHandler(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(InsertBranchProbe request, CancellationToken ct)
    {
        var branch = Branch.Create(_scope.TenantId, request.Code, request.Name);
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct);
        return branch.Id;
    }
}

public record InsertThenThrowProbe(string Code) : IRequest;

public class InsertThenThrowProbeHandler : IRequestHandler<InsertThenThrowProbe>
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    public InsertThenThrowProbeHandler(StoreOsDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(InsertThenThrowProbe request, CancellationToken ct)
    {
        var branch = Branch.Create(_scope.TenantId, request.Code, "Temp Branch");
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct);
        throw new DomainException("test_failure", "Intentional failure after insert");
    }
}

public record TryWriteInReadOnlyProbe : IRequest, IReadOnlyRequest;

public class TryWriteInReadOnlyProbeHandler : IRequestHandler<TryWriteInReadOnlyProbe>
{
    private readonly StoreOsDbContext _db;
    public TryWriteInReadOnlyProbeHandler(StoreOsDbContext db) => _db = db;

    public async Task Handle(TryWriteInReadOnlyProbe request, CancellationToken ct)
    {
        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO branches (id, tenant_id, code, name, is_active, created_at) " +
            "VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000000'::uuid, 'X', 'Test', true, now())", ct);
    }
}

public record NestedResult(object? OuterTx, object? InnerTx, int InnerCount);
public record NestedProbe(string Code) : IRequest<NestedResult>;

public class NestedProbeHandler : IRequestHandler<NestedProbe, NestedResult>
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IMediator _mediator;

    public NestedProbeHandler(StoreOsDbContext db, IAccessScope scope, IMediator mediator)
    {
        _db = db;
        _scope = scope;
        _mediator = mediator;
    }

    public async Task<NestedResult> Handle(NestedProbe request, CancellationToken ct)
    {
        var outerTx = _db.Database.CurrentTransaction;
        var branch = Branch.Create(_scope.TenantId, request.Code, "Nested Branch");
        _db.Branches.Add(branch);
        await _db.SaveChangesAsync(ct);

        var innerCount = await _mediator.Send(new CountBranchesProbe(), ct);
        var innerTx = _db.Database.CurrentTransaction;

        return new NestedResult(outerTx, innerTx, innerCount);
    }
}

public record CountUsersProbe(Guid TenantId) : IRequest<int>, IReadOnlyRequest, IAnonymousTenantRequest;

public class CountUsersProbeHandler : IRequestHandler<CountUsersProbe, int>
{
    private readonly StoreOsDbContext _db;
    public CountUsersProbeHandler(StoreOsDbContext db) => _db = db;
    public Task<int> Handle(CountUsersProbe request, CancellationToken ct) => _db.Users.CountAsync(ct);
}

public record GetReadOnlyIsolationProbe : IRequest<string>, IReadOnlyRequest;

public class GetReadOnlyIsolationProbeHandler : IRequestHandler<GetReadOnlyIsolationProbe, string>
{
    private readonly StoreOsDbContext _db;
    public GetReadOnlyIsolationProbeHandler(StoreOsDbContext db) => _db = db;
    public async Task<string> Handle(GetReadOnlyIsolationProbe request, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SHOW transaction_isolation";
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

public record GetCommandIsolationProbe : IRequest<string>;

public class GetCommandIsolationProbeHandler : IRequestHandler<GetCommandIsolationProbe, string>
{
    private readonly StoreOsDbContext _db;
    public GetCommandIsolationProbeHandler(StoreOsDbContext db) => _db = db;
    public async Task<string> Handle(GetCommandIsolationProbe request, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SHOW transaction_isolation";
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

public record GetSerializableIsolationProbe : IRequest<string>, ISerializableRequest;

public class GetSerializableIsolationProbeHandler : IRequestHandler<GetSerializableIsolationProbe, string>
{
    private readonly StoreOsDbContext _db;
    public GetSerializableIsolationProbeHandler(StoreOsDbContext db) => _db = db;
    public async Task<string> Handle(GetSerializableIsolationProbe request, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
        cmd.CommandText = "SHOW transaction_isolation";
        return (string)(await cmd.ExecuteScalarAsync(ct))!;
    }
}

public class TestScope : IAccessScope
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; } = Guid.NewGuid();
    public HashSet<Guid> BranchIdsSet { get; set; } = new();
    public IReadOnlySet<Guid> BranchIds => BranchIdsSet;
    public bool AllBranches { get; set; } = true;
    public IReadOnlySet<string> Permissions => new HashSet<string>();
    public int TokenVersion => 1;
    public bool IsAuthenticated { get; set; }
    public bool HasTenantContext => TenantId != Guid.Empty;

    public void UseAnonymousTenant(Guid tenantId)
    {
        if (IsAuthenticated) throw new InvalidOperationException("Authenticated scopes cannot switch tenant.");
        TenantId = tenantId;
    }

    public void UseSystemContext(Guid tenantId)
    {
        if (IsAuthenticated) throw new InvalidOperationException("Authenticated scopes cannot switch tenant.");
        TenantId = tenantId;
        AllBranches = true;
    }

    public bool HasBranchAccess(Guid branchId) => AllBranches || BranchIdsSet.Contains(branchId);
    public bool HasPermission(string permissionCode) => true;
}

#endregion

[Collection("postgres")]
public class TransactionBehaviorTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly string _appConnectionString;
    private readonly string _ownerConnectionString;

    private Guid _tenantAId;
    private Guid _tenantBId;

    public TransactionBehaviorTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _appConnectionString = fixture.AppConnectionString;
        _ownerConnectionString = fixture.OwnerConnectionString;
    }

    private ServiceProvider CreateContainer(TestScope scope, int maxPoolSize = 20)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug());

        var cs = new NpgsqlConnectionStringBuilder(_appConnectionString)
        {
            MaxPoolSize = maxPoolSize
        }.ToString();

        services.AddSingleton<IAccessScope>(scope);
        services.AddScoped<RlsSessionInterceptor>();

        services.AddDbContext<StoreOsDbContext>((sp, options) =>
        {
            options.UseNpgsql(cs);
            options.AddInterceptors(sp.GetRequiredService<RlsSessionInterceptor>());
        });

        services.AddScoped<ITenantJobRunner, TenantJobRunner>();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(TransactionBehaviorTests).Assembly);
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        return services.BuildServiceProvider();
    }

    public async Task InitializeAsync()
    {
        _tenantAId = Guid.NewGuid();
        _tenantBId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_ownerConnectionString);
        await conn.OpenAsync();

        var subA = "tx-a-" + _tenantAId.ToString("N")[..8];
        var subB = "tx-b-" + _tenantBId.ToString("N")[..8];

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
            "VALUES (@idA, 'Tenant A', @subA, true, 'Asia/Kolkata', '{}'::jsonb, now()), " +
            "       (@idB, 'Tenant B', @subB, true, 'Asia/Kolkata', '{}'::jsonb, now());", conn);
        cmd.Parameters.AddWithValue("idA", _tenantAId);
        cmd.Parameters.AddWithValue("subA", subA);
        cmd.Parameters.AddWithValue("idB", _tenantBId);
        cmd.Parameters.AddWithValue("subB", subB);
        await cmd.ExecuteNonQueryAsync();

        // Seed 1 user for Tenant A and 2 users for Tenant B
        await using var userCmd = new NpgsqlCommand(
            "INSERT INTO users (id, tenant_id, email, password_hash, display_name, token_version, is_active, created_at) " +
            "VALUES (gen_random_uuid(), @idA, 'userA@storeos.io', 'hash', 'User A', 1, true, now()), " +
            "       (gen_random_uuid(), @idB, 'userB1@storeos.io', 'hash', 'User B1', 1, true, now()), " +
            "       (gen_random_uuid(), @idB, 'userB2@storeos.io', 'hash', 'User B2', 1, true, now());", conn);
        userCmd.Parameters.AddWithValue("idA", _tenantAId);
        userCmd.Parameters.AddWithValue("idB", _tenantBId);
        await userCmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_ownerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM branches WHERE tenant_id IN (@idA, @idB); " +
            "DELETE FROM users WHERE tenant_id IN (@idA, @idB); " +
            "DELETE FROM tenants WHERE id IN (@idA, @idB);", conn);
        cmd.Parameters.AddWithValue("idA", _tenantAId);
        cmd.Parameters.AddWithValue("idB", _tenantBId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task A_ScopedReads_TenantASeesOnlyA_TenantBSeesOnlyB()
    {
        var scopeA = new TestScope { TenantId = _tenantAId, IsAuthenticated = true };
        await using var containerA = CreateContainer(scopeA);
        var mediatorA = containerA.GetRequiredService<IMediator>();

        var scopeB = new TestScope { TenantId = _tenantBId, IsAuthenticated = true };
        await using var containerB = CreateContainer(scopeB);
        var mediatorB = containerB.GetRequiredService<IMediator>();

        // Seed 1 branch for Tenant A and 2 branches for Tenant B
        await mediatorA.Send(new InsertBranchProbe("A01", "Branch A1"));
        await mediatorB.Send(new InsertBranchProbe("B01", "Branch B1"));
        await mediatorB.Send(new InsertBranchProbe("B02", "Branch B2"));

        var countA = await mediatorA.Send(new CountBranchesProbe());
        var countB = await mediatorB.Send(new CountBranchesProbe());

        countA.Should().Be(1);
        countB.Should().Be(2);
    }

    [Fact]
    public async Task B_RollbackOnFailure_LeavesNoRowBehind_AndPreservesException()
    {
        var scope = new TestScope { TenantId = _tenantAId, IsAuthenticated = true };
        await using var container = CreateContainer(scope);
        var mediator = container.GetRequiredService<IMediator>();

        var initialCount = await mediator.Send(new CountBranchesProbe());

        var act = () => mediator.Send(new InsertThenThrowProbe("R01"));
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Intentional failure after insert")
            .Where(e => e.Code == "test_failure");

        var afterCount = await mediator.Send(new CountBranchesProbe());
        afterCount.Should().Be(initialCount);
    }

    [Fact]
    public async Task C_ReadOnlyEnforcement_FailsWithPostgresError25006()
    {
        var scope = new TestScope { TenantId = _tenantAId, IsAuthenticated = true };
        await using var container = CreateContainer(scope);
        var mediator = container.GetRequiredService<IMediator>();

        var act = () => mediator.Send(new TryWriteInReadOnlyProbe());
        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "25006"); // read_only_sql_transaction
    }

    [Fact]
    public async Task D_ConnectionPoolBleed_WithMaxPoolSize1_SecondUnscopedSeesZero()
    {
        var scopeA = new TestScope { TenantId = _tenantAId, IsAuthenticated = true };
        await using var containerA = CreateContainer(scopeA, maxPoolSize: 1);
        var mediatorA = containerA.GetRequiredService<IMediator>();

        // 1. Run request as Tenant A
        await mediatorA.Send(new InsertBranchProbe("P01", "Branch Pool"));
        var countA = await mediatorA.Send(new CountBranchesProbe());
        countA.Should().BeGreaterOrEqualTo(1);

        // 2. Request with NO tenant context on the same pool
        var unscoped = new TestScope { TenantId = Guid.Empty, IsAuthenticated = false };
        await using var containerUnscoped = CreateContainer(unscoped, maxPoolSize: 1);
        var db = containerUnscoped.GetRequiredService<StoreOsDbContext>();

        // Without tenant context, RLS policies fail-closed and return 0
        var branches = await db.Branches.ToListAsync();
        branches.Should().BeEmpty();
    }

    [Fact]
    public async Task E_NestedJoin_SeesOuterInsert_AndSharesSameTransaction()
    {
        var scope = new TestScope { TenantId = _tenantAId, IsAuthenticated = true };
        await using var container = CreateContainer(scope);
        var mediator = container.GetRequiredService<IMediator>();

        var result = await mediator.Send(new NestedProbe("N01"));

        result.OuterTx.Should().NotBeNull();
        result.InnerTx.Should().BeSameAs(result.OuterTx);
        result.InnerCount.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task F_AnonymousFlow_TenantIdA_SeesOnlyA_GuidEmpty_ThrowsDomainException()
    {
        var scope = new TestScope { TenantId = Guid.Empty, IsAuthenticated = false };
        await using var container = CreateContainer(scope);
        var mediator = container.GetRequiredService<IMediator>();

        // 1. With TenantId = Tenant A, anonymous request switches scope and sees Tenant A's 1 user
        var usersA = await mediator.Send(new CountUsersProbe(_tenantAId));
        usersA.Should().Be(1);

        // 2. With TenantId = Guid.Empty, throws DomainException("tenant_required")
        var act = () => mediator.Send(new CountUsersProbe(Guid.Empty));
        await act.Should().ThrowAsync<DomainException>()
            .Where(e => e.Code == "tenant_required");
    }

    [Fact]
    public async Task G_TenantJobRunner_RunsPerTenant_AndRollsBackOnException()
    {
        var scope = new TestScope { TenantId = Guid.Empty, IsAuthenticated = false };
        await using var container = CreateContainer(scope);
        var runner = container.GetRequiredService<ITenantJobRunner>();

        // 1. Run job for Tenant A and insert a branch
        await runner.RunAsync(_tenantAId, async (sp, ct) =>
        {
            var db = sp.GetRequiredService<StoreOsDbContext>();
            db.Branches.Add(Branch.Create(_tenantAId, "J01", "Job Branch A"));
            await db.SaveChangesAsync(ct);
        });

        // 2. Run job for Tenant B: sees 0 branches with code JOB_A
        await runner.RunAsync(_tenantBId, async (sp, ct) =>
        {
            var db = sp.GetRequiredService<StoreOsDbContext>();
            var branches = await db.Branches.Where(b => b.Code == "J01").ToListAsync(ct);
            branches.Should().BeEmpty();
        });

        // 3. Exception inside work rolls back
        var act = () => runner.RunAsync(_tenantAId, async (sp, ct) =>
        {
            var db = sp.GetRequiredService<StoreOsDbContext>();
            db.Branches.Add(Branch.Create(_tenantAId, "F01", "Will rollback"));
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException("Job failure");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Verify FAIL_JOB does not exist
        await runner.RunAsync(_tenantAId, async (sp, ct) =>
        {
            var db = sp.GetRequiredService<StoreOsDbContext>();
            var branches = await db.Branches.Where(b => b.Code == "F01").ToListAsync(ct);
            branches.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task H_IsolationLevels_MatchRequestMarkers()
    {
        var scope = new TestScope { TenantId = _tenantAId, IsAuthenticated = true };
        await using var container = CreateContainer(scope);
        var mediator = container.GetRequiredService<IMediator>();

        var readOnlyIsolation = await mediator.Send(new GetReadOnlyIsolationProbe());
        readOnlyIsolation.ToLowerInvariant().Should().Be("read committed");

        var commandIsolation = await mediator.Send(new GetCommandIsolationProbe());
        commandIsolation.ToLowerInvariant().Should().Be("repeatable read");

        var serializableIsolation = await mediator.Send(new GetSerializableIsolationProbe());
        serializableIsolation.ToLowerInvariant().Should().Be("serializable");
    }
}
