# P-109 — TransactionBehavior — one transaction per request, RLS session variables, tenant job runner

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-109** of the StoreOS Cloud build ("TransactionBehavior — one transaction per request, RLS session variables, tenant job runner") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document. Read `prompts/_shared/CONTRACTS.md` and `prompts/_shared/DECISIONS.md` first: they define the shared interfaces and the decisions this prompt relies on.

## 2. WORLD STATE

- P-112 (correlation id) and P-113 (error pipeline, `DomainException` in `StoreOS.Domain.Exceptions`) are complete.
- P-108 complete: RLS policies exist on the tenant-only tables and are proven fail-closed with the
  NON-superuser `storeos_app` role; `PostgresTestFixture` (P-108) and `NullAccessScope` (P-106) exist.
- `IAccessScope` (P-106) reads JWT claims in memory. Nothing yet tells PostgreSQL which tenant a request
  belongs to: THIS prompt does that.
- This is one of the most architecturally critical prompts in the build (Plan §9.2). The original version put the
  transaction in an HTTP middleware; that design cannot support serialization-failure retry, commits AFTER the
  response is written, rolls back the refresh-token theft revocation, and cannot serve anonymous flows. It has been
  replaced by a MediatR behavior (DECISIONS D-01, D-02). Read the CONTEXT BLOCK twice.

## 3. CONTEXT BLOCK

### 3.1 The rule

**Exactly one database transaction per MediatR request, opened by `TransactionBehavior` (Infrastructure).** Handlers never
call `BeginTransaction`/`Commit`/`Rollback`. The transaction is opened after the RLS session variables can be set and is
committed BEFORE the controller writes the response, so a failed COMMIT (PostgreSQL error 40001 at commit time) is still an
error the pipeline can retry (P-511) and can report; the client never sees a 200 for a rolled-back write.

### 3.2 Marker interfaces (create `backend/src/StoreOS.Application/Abstractions/IRequestMarkers.cs`)

```csharp
namespace StoreOS.Application.Abstractions;

/// Query that only reads tenant data: ReadCommitted, READ ONLY.
public interface IReadOnlyRequest { }
/// Heavy analytics/exports: runs on ReportingDbContext (P-119): RepeatableRead, READ ONLY (one consistent snapshot).
public interface IReportingRequest { }
/// Command that needs SERIALIZABLE isolation (checkout, estimate conversion).
public interface ISerializableRequest { }
```
(`IAnonymousTenantRequest` already exists from P-106.) Commands implement none of `IReadOnlyRequest`/`IReportingRequest`
and get RepeatableRead, read-write. Rule enforced by the architecture test in P-114: every `*Query` implements
`IReadOnlyRequest` or `IReportingRequest`.

| Request marker | DbContext | Isolation | Mode | Retry on 40001/40P01 (P-511) |
|---|---|---|---|---|
| `IReadOnlyRequest` | `StoreOsDbContext` | ReadCommitted | READ ONLY | no |
| `IReportingRequest` | `ReportingDbContext` (P-119) | RepeatableRead | READ ONLY | no |
| `ISerializableRequest` | `StoreOsDbContext` | Serializable | read-write | yes |
| any other request | `StoreOsDbContext` | RepeatableRead | read-write | yes |

(Reports use one RepeatableRead snapshot so that "consolidated report == sum of branch reports" holds even while sales are
being written; this is a deliberate refinement of the plan's ReadCommitted for reads, DECISIONS D-04.)

### 3.3 `RlsSessionInterceptor` (Infrastructure) — sets the session variables when a transaction starts

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class RlsSessionInterceptor : DbTransactionInterceptor
{
    private readonly IAccessScope _scope;
    public RlsSessionInterceptor(IAccessScope scope) => _scope = scope;

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (_scope.HasTenantContext)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = result;
            cmd.CommandText = "SELECT set_config('app.tenant_id', @t, true), set_config('app.branch_ids', @b, true), set_config('app.all_branches', @a, true)";
            Add(cmd, "@t", _scope.TenantId.ToString());
            Add(cmd, "@b", string.Join(",", _scope.BranchIds));
            Add(cmd, "@a", _scope.AllBranches ? "true" : "false");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        return result;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        => TransactionStartedAsync(connection, eventData, result).AsTask().GetAwaiter().GetResult();

    private static void Add(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter(); p.ParameterName = name; p.Value = value; cmd.Parameters.Add(p);
    }
}
```
Register it as SCOPED and attach it to the DbContext: `services.AddScoped<RlsSessionInterceptor>();` and
`AddDbContext<StoreOsDbContext>((sp, o) => o.UseNpgsql(cs).AddInterceptors(sp.GetRequiredService<RlsSessionInterceptor>()))`
(keep the P-101 `IAppDbContext` registration; do NOT call `EnableRetryOnFailure`). `set_config(..., true)` is transaction-local, so
the values disappear at COMMIT/ROLLBACK and cannot bleed to the next user of the pooled connection. NOTE: after a transaction that
used `set_config(..., true)` PostgreSQL leaves the setting as an EMPTY STRING (not NULL) on that pooled connection; the RLS
policies handle that with `NULLIF(current_setting(..., true), '')` (P-108/P-110).

### 3.4 `TransactionBehavior` (Infrastructure)

```csharp
using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class TransactionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IServiceProvider _services;
    private readonly IAccessScope _scope;
    private readonly ILogger<TransactionBehavior<TRequest, TResponse>> _logger;

    public TransactionBehavior(IServiceProvider services, IAccessScope scope,
        ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    { _services = services; _scope = scope; _logger = logger; }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IAnonymousTenantRequest anon)
        {
            if (anon.TenantId == Guid.Empty)
                throw new DomainException("tenant_required", "The tenant could not be determined for this request.");
            _scope.UseAnonymousTenant(anon.TenantId);
        }

        // Requests without any tenant context never touch RLS-protected tables (e.g. tenant lookup by host).
        if (!_scope.HasTenantContext) return await next();

        var db = _services.GetRequiredService<StoreOsDbContext>();
        if (db.Database.CurrentTransaction is not null) return await next();   // nested Send(): join the outer transaction

        var (isolation, readOnly) = request switch
        {
            ISerializableRequest => (IsolationLevel.Serializable, false),
            IReadOnlyRequest => (IsolationLevel.ReadCommitted, true),
            _ => (IsolationLevel.RepeatableRead, false)
        };

        await using var tx = await db.Database.BeginTransactionAsync(isolation, ct);   // interceptor sets app.* here
        try
        {
            if (readOnly) await db.Database.ExecuteSqlRawAsync("SET LOCAL transaction_read_only = on", ct);
            var response = await next();
            await tx.CommitAsync(ct);
            return response;
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollbackEx) { _logger.LogDebug(rollbackEx, "Rollback after failure was not possible (transaction already aborted)."); }
            db.ChangeTracker.Clear();   // a retry (P-511) must never see entities from the failed attempt
            throw;
        }
    }
}
```
Register the behavior with MediatR IN THE ORDER given in DECISIONS D-04 / P-114 (Validation, [SerializationRetry], Transaction, [TokenVersion], Authorization, [Idempotency]); at this prompt only `TransactionBehavior` is added after the existing ones. P-119 replaces the `var db = ...` line with a context selector for `IReportingRequest` (exact snippet is in P-119).

### 3.5 `ITenantJobRunner` (background jobs; used by P-905/P-906/P-902)

`backend/src/StoreOS.Application/Abstractions/ITenantJobRunner.cs`:
```csharp
namespace StoreOS.Application.Abstractions;
public interface ITenantJobRunner
{
    Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct);
}
```
`backend/src/StoreOS.Infrastructure/Persistence/TenantJobRunner.cs`: creates a NEW async DI scope, calls
`scope.ServiceProvider.GetRequiredService<IAccessScope>().UseSystemContext(tenantId)`, opens ONE ReadCommitted transaction on the
scope's `StoreOsDbContext` (interceptor sets the variables), runs `work(scope.ServiceProvider, ct)`, commits; on any exception
rolls back, clears the change tracker and rethrows. Register `AddScoped<ITenantJobRunner, TenantJobRunner>()` (jobs create their own scope).

### 3.6 Pipeline notes for the HTTP layer
There is NO HTTP-level transaction middleware. HTTP middleware order is: CorrelationId -> ExceptionHandling -> Authentication ->
AccessScope -> BranchContext -> Authorization -> controllers. Because the transaction is closed inside MediatR, an exception reaching
`ExceptionHandlingMiddleware` always means the transaction is already rolled back.

## 4. TASK

1. Create `IRequestMarkers.cs`, `ITenantJobRunner.cs` (Application/Abstractions) exactly as shown.
2. Create `RlsSessionInterceptor.cs`, `TransactionBehavior.cs`, `TenantJobRunner.cs` (Infrastructure/Persistence) exactly as shown; register the interceptor,
   the behavior and the job runner; attach the interceptor in the `AddDbContext` call. Update `NullAccessScope`/test doubles if any implement `IAccessScope`.
3. Create the test-only requests inside `StoreOS.IntegrationTests` and build a small DI container per test (`AddDbContext` with `AppConnectionString`,
   the interceptor, MediatR with `TransactionBehavior`, and a test `IAccessScope` implementing ALL members): a query `CountCustomersProbe : IRequest<int>, IReadOnlyRequest`, a command
   `InsertThenThrowProbe : IRequest` (inserts a customer then throws `DomainException`), a command `InsertProbe : IRequest`, a query `TryWriteInReadOnlyProbe : IRequest, IReadOnlyRequest`
   (executes an INSERT), a command `NestedProbe : IRequest<int>` that calls `Send(CountCustomersProbe)` inside its handler, and an anonymous probe `CountUsersProbe : IRequest<int>, IReadOnlyRequest, IAnonymousTenantRequest`.
4. Write `TransactionBehaviorTests` (xUnit, `[Collection("postgres")]`). All of these MUST pass:
   (a) tenant A's scoped query sees only A's rows, tenant B's sees only B's;
   (b) `InsertThenThrowProbe` leaves NO row behind (rollback) and the exception type is preserved;
   (c) `TryWriteInReadOnlyProbe` fails with PostgreSQL error 25006 (read-only transaction);
   (d) connection-pool bleed: with `Maximum Pool Size=1`, run a request as tenant A then a request whose scope has NO tenant context and assert the second sees ZERO rows (not an exception, not A's rows);
   (e) `NestedProbe` sees the outer transaction's uncommitted insert (joined transaction) and there is exactly one `BEGIN` (assert via `db.Database.CurrentTransaction` identity);
   (f) anonymous flow: `CountUsersProbe` with `TenantId=A` sees only A's users; with `Guid.Empty` throws `DomainException{Code="tenant_required"}`;
   (g) `TenantJobRunner`: two tenants, each run sees only its own rows; an exception inside `work` rolls back;
   (h) isolation levels: inside a `ReadOnly` probe `SHOW transaction_isolation` returns `read committed`, inside a plain command `repeatable read`, inside an `ISerializableRequest` `serializable`.
5. `dotnet build` must have zero warnings introduced by this prompt.

## 5. HARD CONSTRAINTS

- `set_config(name, value, true)` (third argument `true` = transaction-local) is the ONLY acceptable way to set the session variables. Never build `SET LOCAL x = '...'` strings by interpolation and never omit `true`.
- Transactions are started only with `db.Database.BeginTransactionAsync(isolationLevel, ct)` inside `TransactionBehavior`/`TenantJobRunner`. No other class in the repository may begin a transaction (the architecture test in P-114 greps for `BeginTransaction` outside these two files and the test project).
- Isolation is chosen by the request marker exactly as in the table. Do NOT try to change the isolation level after opening the transaction (`SET TRANSACTION ISOLATION LEVEL` fails once any query, including `set_config`, has run).
- The RLS session variables must never be set outside a transaction and never at session level.
- `TransactionBehavior` lives in `StoreOS.Infrastructure`; it must not be referenced from `StoreOS.Application`.

Global invariants (apply to all backend code in this repo):

```
- Money: C# decimal ONLY. Never double/float. numeric(18,2) in Postgres.
  Round half-up: MidpointRounding.AwayFromZero, 2 dp, via Money.Round2.
- EMI uses pure decimal exponentiation (DecimalPow loop) — Math.Pow is forbidden.
- Timestamps: timestamptz, stored UTC. Business date computed in tenant timezone
  (Asia/Kolkata default).
- Every tenant table has tenant_id; indexes lead with tenant_id (then branch_id).
- All DB access happens inside transactions with SET LOCAL app.tenant_id / app.branch_ids /
  app.all_branches (RLS session scope). No query outside this scope.
- Validation failures -> 400 with field problems; domain violations -> DomainException -> 422
  with code+message. Never a 500 for business rule violations.
- Authorization runs in MediatR pipeline behaviors, never controllers, never frontend.
- Server recomputes all prices/taxes/totals from DB master data; client-submitted amounts
  are ignored unless an override permission exists.
- Historical snapshots on sale lines are immutable; ledgers and audit_log are append-only.
- tenant_id-scoped RLS must be active on every table containing tenant_id.
- Namespace convention: StoreOS.* everywhere (StoreOS.Domain, StoreOS.Application,
  StoreOS.Infrastructure, StoreOS.Api, StoreOS.Modules). Never PhoneZone.*.
- Layering: StoreOS.Application NEVER references StoreOS.Infrastructure. Application code reaches
  the database only through IAppDbContext and reaches infrastructure services only through
  interfaces declared in StoreOS.Application/Abstractions (see _shared/CONTRACTS.md).
- Transactions: exactly ONE transaction per MediatR request, opened by TransactionBehavior
  (Infrastructure). Handlers never call BeginTransaction/Commit/Rollback. Serialization-failure
  retry wraps the WHOLE transaction (SerializationRetryBehavior is the outermost behavior).
- RLS: policies read session variables ONLY via NULLIF(current_setting('app.x', true), '') and
  never contain an "IS NULL" bypass. Queries implement IReadOnlyRequest, report queries
  IReportingRequest, Serializable commands ISerializableRequest (Application/Abstractions).
```

## 6. FORBIDDEN PATTERNS

```
FORBIDDEN in all code you produce:
- TODO / FIXME / HACK / XXX comments
- NotImplementedException / NotSupportedException / "not implemented" exceptions
- Empty method bodies; methods returning null/0/"" as placeholders for real logic
- "// similar to above", "// rest omitted", "for brevity", ellipses (...) inside code
- Mock/fake/in-memory service implementations in production code (test projects ONLY)
- Hardcoded demo data in production code paths
- Angular: "coming soon" templates, buttons with empty (click) handlers, services
  returning of(mockData) instead of real HTTP calls
If you cannot fully implement something: STOP and write "BLOCKED: <reason>" as the first line below the `---`
separator in docs/BUILD_LOG.md. Never silently stub. Before finishing, grep your created/modified files
for the forbidden patterns above and fix every hit before running verification.
```

## 7. VERIFICATION

```bash
cd store-os-cloud
dotnet build StoreOS.sln
dotnet test backend/tests/StoreOS.IntegrationTests --filter "FullyQualifiedName~TransactionBehaviorTests"
# Expected: all 8 test groups pass (scoped reads, rollback, read-only, pool bleed, nested join, anonymous flow, job runner, isolation levels).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-109 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-109 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-109` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact `RlsSessionInterceptor`, `TransactionBehavior`, `TenantJobRunner`, `IRequestMarkers` and `ITenantJobRunner` signatures and file locations, the MediatR behavior registration order, the HTTP middleware order in `Program.cs`, and the isolation table.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-109: <one-line summary>"` only after verification is
   fully green.
