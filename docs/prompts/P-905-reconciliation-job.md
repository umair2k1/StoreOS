# P-905 — Nightly reconciliation Hangfire job (5 sweeps)

**Phase:** 9 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-905** of the StoreOS Cloud build ("Nightly reconciliation Hangfire job (5 sweeps)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-904 complete: full migration tool with dedup and conflict resolution
  works.
- No nightly reconciliation job exists yet (Requirements Section 41.2, Plan Section 8.9/10.8) -
  the five-sweep automated consistency check that is one of the product's most important
  data-integrity guarantees.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** also add a daily job `IdempotencyKeyPurgeJob` that deletes `idempotency_keys` rows older than 7 days for each tenant (through `ITenantJobRunner`).

> **REVISION NOTE (review v2):** with FORCE ROW LEVEL SECURITY a background job that sets no `app.tenant_id` sees ZERO rows. Enumerate tenants from the (RLS-free) `tenants` table, then for EACH tenant run the sweeps through `ITenantJobRunner.RunAsync(tenantId, work, ct)` (create it in P-109: it opens a fresh DI scope, sets a `JobAccessScope` with `TenantId=<tenant>`, `AllBranches=true`, opens ONE ReadCommitted transaction with `set_config`, and runs `work`). A failure in one tenant is logged and does not stop the others. Test with two tenants and assert each sweep only sees its own tenant's rows.

Add `Hangfire.AspNetCore`/`Hangfire.PostgreSql` (already pinned) to
`StoreOS.Api.csproj` and `StoreOS.Infrastructure.csproj`. Wire Hangfire storage pointed at the
SAME Postgres database (a dedicated `hangfire` schema, Hangfire creates its own tables
automatically on first use):

```csharp
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default"))));
builder.Services.AddHangfireServer();
```

Create `backend/src/StoreOS.Infrastructure/Jobs/ReconciliationJob.cs` implementing Plan Section
8.9/10.8's exact 5 sweeps, run PER TENANT PER BRANCH where applicable:

```csharp
public class ReconciliationJob
{
    // constructor injects a DbContextFactory or resolves a fresh scope per tenant, since a
    // background job has no per-request IAccessScope - use IgnoreQueryFilters() throughout,
    // explicitly scoped by an outer loop over all tenants/branches, since this IS a legitimate,
    // documented cross-tenant background-job context (the ONLY place in the codebase where
    // iterating ALL tenants is correct and necessary).

    public async Task RunAsync(CancellationToken ct)
    {
        var tenants = await _db.Tenants.IgnoreQueryFilters().Where(t => t.IsActive).ToListAsync(ct);
        foreach (var tenant in tenants)
        {
            var branches = await _db.Branches.IgnoreQueryFilters().Where(b => b.TenantId == tenant.Id).ToListAsync(ct);
            foreach (var branch in branches)
                await ReconcileStockAsync(tenant.Id, branch.Id, ct); // Sweep 1

            await ReconcileSalePaymentsAsync(tenant.Id, ct);          // Sweep 2
            await ReconcileCustomerRevenueAsync(tenant.Id, ct);       // Sweep 3
            await ReconcileCustomerCreditAsync(tenant.Id, ct);        // Sweep 4
            await ReconcileSupplierPayablesAsync(tenant.Id, ct);      // Sweep 5
        }
    }

    // Sweep 1: branch_inventory.stock_qty == SUM(inventory_movements.qty_delta) per product/branch
    // Sweep 2: sales.amount_paid == SUM(customer_payments.amount) per sale
    // Sweep 3: customers.total_purchases == SUM(sales.grand_total WHERE status='Active') per customer
    // Sweep 4: derived customer credit == SUM(customer_ledger_entries.debit - credit) per customer
    // Sweep 5: derived supplier payable == SUM(supplier_ledger_entries.credit - debit) per supplier
}
```

Write out the FULL implementation of all 5 sweep methods yourself, each comparing the STORED
projection column against the DERIVED sum from the append-only ledger/movement table, and on
ANY mismatch, writing a HIGH-SEVERITY entry via `IAuditWriter` (action:
`"reconciliation.drift_detected"`, changes payload including the entity id, expected vs. actual
values) - per Plan Section 10.8's "Emit alert via OpenTelemetry metric + create high-severity
diagnostic entry," ALSO emit an OpenTelemetry metric (`Meter.CreateCounter<int>
("reconciliation_drift_total").Add(1, tags...)`) for each detected mismatch (wire a basic
`System.Diagnostics.Metrics.Meter` instance, registered once, incremented here).

Register the job to run nightly via Hangfire's recurring-job API in `Program.cs`:
```csharp
RecurringJob.AddOrUpdate<ReconciliationJob>("nightly-reconciliation", job => job.RunAsync(CancellationToken.None), Cron.Daily);
```

## 4. TASK

1. Add Hangfire packages/wiring to `Program.cs` exactly as shown, pointed at the
   main Postgres connection string.
2. Create `ReconciliationJob.cs` in `StoreOS.Infrastructure/Jobs/`, implementing all 5 sweeps in
   full, each writing an audit-log entry AND incrementing an OpenTelemetry counter on any
   detected drift.
3. Register the nightly recurring job.
4. Write `StoreOS.IntegrationTests/Platform/ReconciliationJobTests.cs`: (a) a CONSISTENT dataset
   (no seeded drift) produces ZERO audit-log `reconciliation.drift_detected` entries across all
   5 sweeps; (b) DELIBERATELY seed a drift for EACH of the 5 sweeps independently (e.g. directly
   UPDATE `branch_inventory.stock_qty` via raw SQL to a wrong value without a matching movement,
   bypassing `IInventoryStockService` on purpose to simulate real-world drift) and confirm the
   job detects and logs EACH one with the correct entity id and expected-vs-actual values; (c)
   the job correctly iterates ALL active tenants and their branches, proving it use is a
   legitimate cross-tenant background context (seed two separate tenants each with their own
   drift, confirm both are detected independently); (d) confirm the job does NOT crash or stop
   early if one tenant's data has an unexpected issue - it should continue processing remaining
   tenants (wrap each tenant's processing in its own try/catch, logging a failure for that
   tenant specifically without aborting the whole run).

## 5. HARD CONSTRAINTS

- This job is the ONE OTHER legitimate, documented place (besides `LoginHandler`,
  the tenant-global balance/payable services, and the tenant-export handler) where
  `IgnoreQueryFilters()` combined with an explicit outer loop over ALL tenants is correct and
  necessary - a background job inherently has no single-tenant `IAccessScope`.
- All 5 sweeps must be implemented exactly as specified - do not skip or approximate any of
  them.
- A failure processing one tenant must not abort the whole nightly run for every other tenant -
  isolate failures per-tenant.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ReconciliationJobTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-905 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-905 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-905` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ReconciliationJob` class and all 5 sweep method signatures, the
Hangfire recurring-job registration, and confirmation per-tenant failure isolation works.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-905: <one-line summary>"` only after verification is
   fully green.
