# P-107 — EF Core global query filters (tenant+branch) wiring

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-107** of the StoreOS Cloud build ("EF Core global query filters (tenant+branch) wiring") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-106 complete: `IAccessScope` exists and is populated per-request from JWT
  claims (test-harness proven).
- No entity currently has `tenant_id`/`branch_id` besides `Branch` (tenant-only, since a
  branch's own scope IS the tenant) and `Setting`/`UserRoleAssignment` (already tenant+branch
  shaped but not yet filtered). This prompt establishes the REUSABLE PATTERN for query
  filters; it is applied here to `Branch` (tenant-only pattern) and `Setting`
  (tenant+branch pattern) as the two worked examples, and every later entity-creation prompt
  (P-401 onward) must apply the same pattern to its own new entities.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** when you change the `StoreOsDbContext` constructor to `(DbContextOptions<StoreOsDbContext> options, IAccessScope scope)`, ALSO update `StoreOsDbContextFactory` (P-101) to `return new StoreOsDbContext(options, new NullAccessScope());` (NullAccessScope is created in P-106). Without this edit `dotnet ef` stops compiling.

In `StoreOsDbContext`, inject `IAccessScope` via constructor and apply
query filters in `OnModelCreating`. Exact pattern (Plan §9.1):

```csharp
public class StoreOsDbContext : DbContext
{
    private readonly IAccessScope _scope;

    public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options, IAccessScope scope)
        : base(options)
    {
        _scope = scope;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);

        // Tenant-only filter archetype (Plan §9.1) — applied to entities that have
        // TenantId but no BranchId (e.g. Branch itself, Setting's tenant-level rows via a
        // separate path, Customer, Product, Supplier once they exist).
        modelBuilder.Entity<Branch>().HasQueryFilter(b => b.TenantId == _scope.TenantId);

        // Tenant+branch filter archetype — applied to entities that carry BOTH TenantId
        // (implicitly via the owning aggregate, e.g. Setting has no direct TenantId+BranchId
        // pair that fits this shape cleanly, so Setting uses a tenant-only filter on
        // TenantId with BranchId left unfiltered at the query-filter level (NULL vs. a
        // specific branch is a query-time choice the caller makes explicitly, not a blanket
        // filter) — DO NOT apply a branch filter to Setting.
        modelBuilder.Entity<Setting>().HasQueryFilter(s => s.TenantId == _scope.TenantId);
    }
}
```

Document the REUSABLE RULE every later prompt must follow, verbatim, in
`docs/ARTIFACTS.md` under a `## Query Filter Convention` heading:

```text
For every new entity E added in any later prompt:
- If E has ONLY TenantId (no BranchId): add
  modelBuilder.Entity<E>().HasQueryFilter(x => x.TenantId == _scope.TenantId);
- If E has BOTH TenantId and BranchId (operational/branch-scoped data): add
  modelBuilder.Entity<E>().HasQueryFilter(x =>
      x.TenantId == _scope.TenantId &&
      (_scope.AllBranches || _scope.BranchIds.Contains(x.BranchId)));
- If E has a NULLABLE BranchId representing "head office / tenant-wide" (e.g. expenses,
  supplier_transactions): add
  modelBuilder.Entity<E>().HasQueryFilter(x =>
      x.TenantId == _scope.TenantId &&
      (x.BranchId == null || _scope.AllBranches || _scope.BranchIds.Contains(x.BranchId.Value)));
- These EF filters are LAYER ONE of defense. Layer TWO is PostgreSQL RLS (P-108/P-110).
  Both must independently enforce the same isolation — never rely on only one.
```

## 4. TASK

1. Modify `StoreOsDbContext`'s constructor to accept `IAccessScope scope` and store it
   in a private field, exactly as shown.
2. Apply the tenant-only query filter to `Branch` and `Setting` exactly as shown (Setting
   gets tenant-only, NOT tenant+branch, per the note above).
3. Update the `AddDbContext<StoreOsDbContext>` registration in `Program.cs` — no change
   needed since `IAccessScope` is already registered scoped in P-106 and EF will resolve it
   automatically via constructor injection as long as `AddDbContext` uses the default scoped
   lifetime (confirm this is the case; do not switch to `AddDbContextPool`, which is
   incompatible with scoped-service constructor injection into the context itself).
4. Append the "Query Filter Convention" block to `docs/ARTIFACTS.md` verbatim as shown — this
   is a load-bearing convention document, not just a report artifact.
5. Write `StoreOS.IntegrationTests/Security/QueryFilterTests.cs`: seed two tenants' worth of
   `Branch` rows directly via a raw-SQL bypass (or via a second `DbContext` instance
   constructed with a fake `IAccessScope` for tenant A at seed time), then resolve
   `StoreOsDbContext` scoped to tenant B's `IAccessScope` and assert `context.Branches
   .ToList()` returns ONLY tenant B's rows (proving cross-tenant leakage is blocked at the EF
   layer, independent of RLS which doesn't exist yet).

## 5. HARD CONSTRAINTS

- `AddDbContext` (not `AddDbContextPool`) must be used specifically
  because `StoreOsDbContext` now depends on the scoped `IAccessScope` — pooling contexts with
  per-request scoped dependencies causes stale-scope bugs. This is a permanent constraint for
  this project.
- EF query filters are correctness-critical but NOT sufficient alone — P-108/P-110 add RLS as
  an independent second layer. Do not treat this prompt as "done" for tenant isolation; the
  full guarantee requires both layers.
- Do not add `IgnoreQueryFilters()` anywhere in production code paths (only acceptable inside
  test setup/seed helpers that explicitly need to write cross-tenant fixture data).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter QueryFilterTests
# Expected: cross-tenant leakage test passes (tenant B's context sees 0 of tenant A's rows).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-107 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-107 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-107` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the updated `StoreOsDbContext` constructor signature, and the full "Query
Filter Convention" text block (verbatim, since every future entity-adding prompt must copy
this pattern exactly).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-107: <one-line summary>"` only after verification is
   fully green.
