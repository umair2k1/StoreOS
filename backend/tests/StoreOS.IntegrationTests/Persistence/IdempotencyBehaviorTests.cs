using System.Data;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Behaviors;
using StoreOS.Domain.Exceptions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Persistence;

public record CounterResult(int Count);

public record IncrementProbe(
    string? IdempotencyKey,
    string IdempotencyScope,
    bool IdempotencyRequired,
    int Amount = 1
) : IRequest<CounterResult>, IIdempotentRequest;

public class IncrementProbeHandler : IRequestHandler<IncrementProbe, CounterResult>
{
    private readonly StoreOsDbContext _db;
    public static int ExecutionCount = 0;

    public IncrementProbeHandler(StoreOsDbContext db) => _db = db;

    public async Task<CounterResult> Handle(IncrementProbe request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref ExecutionCount);

        if (request.Amount < 0)
        {
            throw new InvalidOperationException("Negative amount simulation error");
        }

        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO rls_probe_branch_scoped (id, tenant_id, branch_id, label) VALUES (gen_random_uuid(), NULLIF(current_setting('app.tenant_id', true), '')::uuid, '00000000-0000-0000-0000-000000000000'::uuid, 'probe');",
            cancellationToken);

        var count = await _db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM rls_probe_branch_scoped").FirstAsync(cancellationToken);
        return new CounterResult(count);
    }
}

[Collection("postgres")]
public class IdempotencyBehaviorTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _userAId = Guid.NewGuid();
    private readonly Guid _userBId = Guid.NewGuid();

    public IdempotencyBehaviorTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        var subA = "idem-a-" + _tenantAId.ToString("N")[..8];
        var subB = "idem-b-" + _tenantBId.ToString("N")[..8];

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
            "VALUES (@tA, 'Idem Tenant A', @subA, true, 'Asia/Kolkata', '{}'::jsonb, now()), " +
            "       (@tB, 'Idem Tenant B', @subB, true, 'Asia/Kolkata', '{}'::jsonb, now());", conn);
        cmd.Parameters.AddWithValue("tA", _tenantAId);
        cmd.Parameters.AddWithValue("subA", subA);
        cmd.Parameters.AddWithValue("tB", _tenantBId);
        cmd.Parameters.AddWithValue("subB", subB);
        await cmd.ExecuteNonQueryAsync();

        IncrementProbeHandler.ExecutionCount = 0;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM idempotency_keys WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM rls_probe_branch_scoped WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM tenants WHERE id IN (@tA, @tB);", conn);
        cmd.Parameters.AddWithValue("tA", _tenantAId);
        cmd.Parameters.AddWithValue("tB", _tenantBId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public TestAccessScope(Guid tenantId, Guid userId)
        {
            TenantId = tenantId;
            UserId = userId;
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

    private ServiceProvider BuildServiceProvider(Guid tenantId, Guid userId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var scope = new TestAccessScope(tenantId, userId);

        services.AddSingleton<IAccessScope>(scope);
        services.AddScoped<IdempotencyReplayFlag>();

        var interceptor = new RlsSessionInterceptor(scope);
        services.AddDbContext<StoreOsDbContext>(options =>
        {
            options.UseNpgsql(_fixture.AppConnectionString);
            options.AddInterceptors(interceptor);
        });

        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IncrementProbeHandler).Assembly);
            cfg.AddOpenBehavior(typeof(SerializationRetryBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(IdempotencyBehavior<,>));
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Same_key_and_same_payload_twice_executes_handler_once_and_replays()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        using var scope1 = sp.CreateScope();
        var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var flag1 = scope1.ServiceProvider.GetRequiredService<IdempotencyReplayFlag>();

        var key = Guid.NewGuid().ToString();
        var cmd = new IncrementProbe(key, "pos.checkout", true, 1);

        var result1 = await mediator1.Send(cmd);
        flag1.Replayed.Should().BeFalse();
        result1.Count.Should().Be(1);

        using var scope2 = sp.CreateScope();
        var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
        var flag2 = scope2.ServiceProvider.GetRequiredService<IdempotencyReplayFlag>();

        var result2 = await mediator2.Send(cmd);
        flag2.Replayed.Should().BeTrue();
        result2.Should().BeEquivalentTo(result1);

        IncrementProbeHandler.ExecutionCount.Should().Be(1, "handler must run exactly once across identical idempotent requests");
    }

    [Fact]
    public async Task Same_key_with_different_payload_throws_idempotency_key_reuse()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        var key = Guid.NewGuid().ToString();

        using (var scope1 = sp.CreateScope())
        {
            var mediator = scope1.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(new IncrementProbe(key, "pos.checkout", true, 1));
        }

        using (var scope2 = sp.CreateScope())
        {
            var mediator = scope2.ServiceProvider.GetRequiredService<IMediator>();
            var act = () => mediator.Send(new IncrementProbe(key, "pos.checkout", true, 2));

            var ex = await act.Should().ThrowAsync<DomainException>();
            ex.Which.Code.Should().Be("idempotency_key_reuse");
        }
    }

    [Fact]
    public async Task Missing_key_with_required_throws_idempotency_key_required()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var act = () => mediator.Send(new IncrementProbe(null, "pos.checkout", true, 1));
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("idempotency_key_required");
    }

    [Fact]
    public async Task Malformed_key_throws_idempotency_key_invalid()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        using var scope = sp.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var act = () => mediator.Send(new IncrementProbe("not-a-valid-uuid", "pos.checkout", true, 1));
        var ex = await act.Should().ThrowAsync<DomainException>();
        ex.Which.Code.Should().Be("idempotency_key_invalid");
    }

    [Fact]
    public async Task Handler_failure_rolls_back_and_leaves_no_idempotency_row()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        var key = Guid.NewGuid().ToString();

        using (var scope1 = sp.CreateScope())
        {
            var mediator = scope1.ServiceProvider.GetRequiredService<IMediator>();
            var act = () => mediator.Send(new IncrementProbe(key, "pos.checkout", true, -1));
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        await using (var checkConn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await checkConn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT count(*) FROM idempotency_keys WHERE key = @k;", checkConn);
            cmd.Parameters.AddWithValue("k", key);
            var count = (long)(await cmd.ExecuteScalarAsync())!;
            count.Should().Be(0, "failed handler execution must roll back idempotency row completely");
        }

        // Retry with same key succeeds and executes
        using (var scope2 = sp.CreateScope())
        {
            var mediator = scope2.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.Send(new IncrementProbe(key, "pos.checkout", true, 1));
            result.Count.Should().Be(1);
        }
    }

    [Fact]
    public async Task Two_concurrent_requests_with_same_key_resolve_via_retry_and_replay()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        var key = Guid.NewGuid().ToString();
        var cmd = new IncrementProbe(key, "pos.checkout", true, 1);

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();

        var t1 = Task.Run(() => mediator1.Send(cmd));
        var t2 = Task.Run(() => mediator2.Send(cmd));

        var results = await Task.WhenAll(t1, t2);
        results[0].Should().BeEquivalentTo(results[1]);
        IncrementProbeHandler.ExecutionCount.Should().Be(1);
    }

    [Fact]
    public async Task Tenant_B_cannot_replay_tenant_A_key()
    {
        var key = Guid.NewGuid().ToString();

        var spA = BuildServiceProvider(_tenantAId, _userAId);
        using (var scopeA = spA.CreateScope())
        {
            var mediatorA = scopeA.ServiceProvider.GetRequiredService<IMediator>();
            var resA = await mediatorA.Send(new IncrementProbe(key, "pos.checkout", true, 1));
            resA.Count.Should().Be(1);
        }

        var spB = BuildServiceProvider(_tenantBId, _userBId);
        using (var scopeB = spB.CreateScope())
        {
            var mediatorB = scopeB.ServiceProvider.GetRequiredService<IMediator>();
            var flagB = scopeB.ServiceProvider.GetRequiredService<IdempotencyReplayFlag>();

            var resB = await mediatorB.Send(new IncrementProbe(key, "pos.checkout", true, 1));
            flagB.Replayed.Should().BeFalse("Tenant B should execute independently, not replay Tenant A key");
            resB.Count.Should().Be(1);
        }
    }

    [Fact]
    public async Task Same_key_on_different_scopes_is_independent()
    {
        var sp = BuildServiceProvider(_tenantAId, _userAId);
        var key = Guid.NewGuid().ToString();

        using (var scope1 = sp.CreateScope())
        {
            var mediator = scope1.ServiceProvider.GetRequiredService<IMediator>();
            var res1 = await mediator.Send(new IncrementProbe(key, "pos.checkout", true, 1));
            res1.Count.Should().Be(1);
        }

        using (var scope2 = sp.CreateScope())
        {
            var mediator = scope2.ServiceProvider.GetRequiredService<IMediator>();
            var flag2 = scope2.ServiceProvider.GetRequiredService<IdempotencyReplayFlag>();

            var res2 = await mediator.Send(new IncrementProbe(key, "sales.payment", true, 1));
            flag2.Replayed.Should().BeFalse("Different scope with same key must execute independently");
            res2.Count.Should().Be(2);
        }
    }
}
