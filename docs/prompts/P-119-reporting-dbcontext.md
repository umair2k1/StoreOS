# P-119 — ReportingDbContext seam + connection string wiring

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-119** of the StoreOS Cloud build ("ReportingDbContext seam + connection string wiring") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-101–118 complete: full backbone (transactions, RLS, correlation, errors,
  MediatR pipeline, permissions, audit, idempotency, money utility) is in place.
- Only one DbContext (`StoreOsDbContext`) exists so far, used for both writes and reads. This
  prompt introduces the READ-ONLY reporting seam described in Plan §2 Decision 19 / §4.3,
  which from day one points at the SAME database (no read replica exists yet) but is
  architecturally separate so it can be re-pointed to a replica later with zero code changes
  elsewhere.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** Report requests (`IReportingRequest`, P-109) must actually run on this context. Do three things in addition to the text below: (1) create `StoreOS.Application/Abstractions/IReportingDbContext.cs` (`public interface IReportingDbContext { DbSet<TEntity> Set<TEntity>() where TEntity : class; }`) and make `ReportingDbContext : DbContext, IReportingDbContext`; report handlers (P-8xx) inject `IReportingDbContext`, never `ReportingDbContext`. (2) In `TransactionBehavior` (P-109) replace the line `var db = _services.GetRequiredService<StoreOsDbContext>();` by `DbContext db = request is IReportingRequest ? _services.GetRequiredService<ReportingDbContext>() : _services.GetRequiredService<StoreOsDbContext>();` and add the switch arm `IReportingRequest => (IsolationLevel.RepeatableRead, true),` BEFORE the `IReadOnlyRequest` arm. (3) The `AddDbContext<ReportingDbContext>` registration adds the same scoped `RlsSessionInterceptor` and uses `ConnectionStrings:Reporting` (role `storeos_app`). There is no separate HTTP-level transaction for reports any more. Add a test: an `IReportingRequest` probe executes `SHOW transaction_read_only` => `on` and `SHOW transaction_isolation` => `repeatable read`, and returns only its own tenant's rows.

Create `backend/src/StoreOS.Infrastructure/Persistence/ReportingDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public class ReportingDbContext : DbContext
{
    private readonly IAccessScope _scope;

    public ReportingDbContext(DbContextOptions<ReportingDbContext> options, IAccessScope scope)
        : base(options)
    {
        _scope = scope;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        // IMPORTANT: ReportingDbContext maps the SAME physical tables as StoreOsDbContext but
        // is a SEPARATE EF model. It reuses the same IEntityTypeConfiguration<T> classes so
        // schema stays in sync automatically — never hand-maintain a second set of mappings.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);
        // Apply the SAME query filters as StoreOsDbContext for every entity that has one,
        // since RLS session variables (set by the SAME RlsSessionInterceptor, reused here)
        // are the primary defense and EF filters are the second — both contexts must carry
        // identical filters. As entities are added in later prompts, their filter
        // registration lines must be duplicated into BOTH OnModelCreating methods, OR
        // (preferred, to avoid drift) factored into a shared static method
        // StoreOS.Infrastructure.Persistence.QueryFilters.ApplyAll(modelBuilder, scope) that
        // both DbContexts call. Refactor to this shared method NOW as part of this prompt.
    }
}
```

Create `backend/src/StoreOS.Infrastructure/Persistence/QueryFilters.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence;

public static class QueryFilters
{
    public static void ApplyAll(ModelBuilder modelBuilder, IAccessScope scope)
    {
        modelBuilder.Entity<Branch>().HasQueryFilter(b => b.TenantId == scope.TenantId);
        modelBuilder.Entity<Setting>().HasQueryFilter(s => s.TenantId == scope.TenantId);
        // Every later prompt that adds a new tenant-scoped or branch-scoped entity MUST add
        // its HasQueryFilter(...) line HERE, in this single shared method, so both
        // StoreOsDbContext and ReportingDbContext automatically stay consistent. Do not add
        // query filters directly inside either DbContext's OnModelCreating from this point
        // forward.
    }
}
```

Update `StoreOsDbContext.OnModelCreating` to call `QueryFilters.ApplyAll(modelBuilder,
_scope);` instead of the inline filter lines added in P-107 (remove the duplicated inline
calls, replace with the shared method call). `ReportingDbContext.OnModelCreating` calls the
same method.

Register in `Program.cs`:
```csharp
builder.Services.AddDbContext<ReportingDbContext>((sp, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("Reporting"),
        npgsql => { });   // no EnableRetryOnFailure (see P-101)
});
```
Note `ReportingDbContext` does NOT get the `RlsSessionInterceptor` added the same way as
`StoreOsDbContext` in P-109 — actually it MUST, since RLS session variables must be set for
reporting reads too, otherwise `ReportingDbContext` would see zero rows (fail-closed) for
every query. Add the SAME interceptor registration:
```csharp
options.AddInterceptors(sp.GetRequiredService<RlsSessionInterceptor>());
```
And ensure `TenantTransactionMiddleware` (P-109) ALSO begins a transaction on
`ReportingDbContext` when a reporting endpoint is hit — since a single HTTP request only uses
ONE of the two contexts in practice (reporting endpoints inject `ReportingDbContext`, everything
else injects `StoreOsDbContext`), update `TenantTransactionMiddleware` to accept BOTH
contexts via constructor/method injection and begin a transaction on whichever one(s) are
actually resolved during the request. Simplify by having the middleware begin transactions on
BOTH contexts unconditionally at the start of every request (harmless if a context is never
touched — EF won't open a physical connection until first use) — update
`TenantTransactionMiddleware.InvokeAsync` to accept `StoreOsDbContext db, ReportingDbContext
reportingDb` as additional parameters and wrap both in their own transactions with matching
isolation-level logic (Reporting is always `ReadCommitted` regardless of HTTP method, since it
is read-only by contract).

## 4. TASK

1. Create `QueryFilters.cs` and refactor `StoreOsDbContext` to use it (removing
   inline filter duplication from P-107/P-110).
2. Create `ReportingDbContext.cs` exactly as shown, using the shared `QueryFilters.ApplyAll`.
3. Register `ReportingDbContext` in `Program.cs` with the `Reporting` connection string and
   the same `RlsSessionInterceptor`.
4. Update `TenantTransactionMiddleware` to open a `ReadCommitted` transaction on
   `ReportingDbContext` in addition to its existing `StoreOsDbContext` transaction handling,
   committing/rolling back both together based on the same response-status-code check.
5. Write `StoreOS.IntegrationTests/Persistence/ReportingDbContextTests.cs`: seed a `Branch`
   row via `StoreOsDbContext` (write side), commit, then read it back via a freshly resolved
   `ReportingDbContext` scoped to the SAME tenant/branch access — assert it is visible and
   that `ChangeTracker.Entries().Count() == 0` after the query (proving
   `NoTracking` is active). Also confirm cross-tenant isolation holds identically to
   `StoreOsDbContext` (reuse the P-107 test pattern).

## 5. HARD CONSTRAINTS

- `ReportingDbContext` MUST use `QueryTrackingBehavior.NoTracking`
  globally — never track entities read for reporting purposes, this is a hard performance and
  correctness rule (reports never write back through this context).
- Both DbContexts must share `QueryFilters.ApplyAll` — never let their entity filter sets
  drift apart, or reporting queries could see rows that operational queries would not (or vice
  versa), which would be a data-consistency defect.
- `ConnectionStrings:Reporting` points at the SAME database as `Default` for now (no replica
  exists) — this is intentional per Plan §2 Decision 19; do not fabricate a second database.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ReportingDbContextTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter QueryFilterTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
# Expected: all green; no regression in existing tenant-isolation tests.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-119 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-119 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-119` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ReportingDbContext` and `QueryFilters.ApplyAll` signatures, and the
explicit instruction (repeat it verbatim) that every future entity-adding prompt must add its
query filter line to `QueryFilters.ApplyAll`, never inline in either DbContext.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-119: <one-line summary>"` only after verification is
   fully green.
