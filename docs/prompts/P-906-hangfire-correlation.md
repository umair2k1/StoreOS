# P-906 — Hangfire wiring + correlation-id propagation into jobs

**Phase:** 9 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-906** of the StoreOS Cloud build ("Hangfire wiring + correlation-id propagation into jobs") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-905 complete: the nightly reconciliation job runs all 5 sweeps
  correctly with per-tenant isolation.
- Hangfire is wired but background jobs do NOT yet carry a correlation id into their execution
  context or Serilog log scope (Plan Section 6.7 point 5: "Background job propagation"). This
  is the final Phase 9 prompt, closing that observability gap.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** jobs must go through `ITenantJobRunner` (P-109) so RLS context and the correlation id are both set inside the job's transaction.

Create `backend/src/StoreOS.Api/Jobs/HangfireSetup.cs` centralizing the
Hangfire configuration from P-905 (extract it out of `Program.cs` into this dedicated file for
clarity) and adding a Hangfire JOB FILTER that establishes correlation-id propagation:

```csharp
using Hangfire.Common;
using Hangfire.Server;
using Serilog.Context;

namespace StoreOS.Api.Jobs;

public class CorrelationIdJobFilter : JobFilterAttribute, IServerFilter
{
    public void OnPerforming(PerformingContext context)
    {
        var correlationId = context.BackgroundJob.Job.Args
            .OfType<string>()
            .FirstOrDefault(a => Guid.TryParse(a, out _)) ?? Guid.NewGuid().ToString("D");
        context.Items["CorrelationId"] = correlationId;
        LogContext.PushProperty("CorrelationId", correlationId);
    }

    public void OnPerformed(PerformingContext context) { }
}
```

Note this simplistic arg-scanning approach is FRAGILE (matching ANY string-typed argument that
happens to parse as a GUID could false-positive) - implement a MORE ROBUST approach instead:
define an explicit wrapper pattern where any job method that should carry a correlation id
accepts it as its LAST, EXPLICITLY NAMED parameter:

```csharp
public class ReconciliationJob
{
    public async Task RunAsync(string correlationId, CancellationToken ct)
    {
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            // ... existing sweep logic from P-905, now inside this using block so every log
            // line emitted during the run carries the correlation id.
        }
    }
}
```

Update the Hangfire recurring-job registration to generate a fresh correlation id for each
scheduled (non-request-triggered) run:
```csharp
RecurringJob.AddOrUpdate<ReconciliationJob>("nightly-reconciliation",
    job => job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None), Cron.Daily);
```

For jobs ENQUEUED FROM a live HTTP request (none exist yet in this manifest, but the mechanism
must be ready for future use - e.g. a future "generate export in background" job), demonstrate
the pattern with a small, genuinely useful example: convert the reconciliation job so it CAN
ALSO be triggered on-demand via a new admin endpoint
`POST /api/platform/reconciliation/run-now` (`IHighImpactOperation`, `settings:manage`), which
enqueues the SAME `ReconciliationJob.RunAsync` via `BackgroundJob.Enqueue<ReconciliationJob>(job
=> job.RunAsync(correlationContext.CorrelationId, CancellationToken.None))`, passing the
TRIGGERING REQUEST's own correlation id (from `ICorrelationContextAccessor`, P-112) into the
job, so a manually-triggered run's logs/audit entries can be traced back to the specific admin
request that triggered it.

## 4. TASK

1. Create `HangfireSetup.cs` in `StoreOS.Api/Jobs/`, moving the Hangfire
   configuration out of `Program.cs` into this dedicated file (call it from `Program.cs`).
2. Update `ReconciliationJob.RunAsync` to accept an explicit `correlationId` parameter and wrap
   its body in `LogContext.PushProperty("CorrelationId", correlationId)`, exactly as shown.
3. Update the nightly recurring-job registration to generate a fresh correlation id per
   scheduled run.
4. Create the `POST /api/platform/reconciliation/run-now` endpoint on `PlatformController.cs`,
   enqueuing the SAME job with the TRIGGERING request's own correlation id.
5. Write `StoreOS.IntegrationTests/Platform/ReconciliationCorrelationTests.cs`: (a) triggering
   `run-now` with a KNOWN `X-Correlation-ID` header results (after waiting for the Hangfire job
   to actually execute - poll briefly or run it inline/synchronously in the test via a direct
   `ReconciliationJob.RunAsync(correlationId, ct)` call rather than relying on Hangfire's own
   async worker timing, for test determinism) in `audit_log` entries (if any drift was seeded)
   carrying that SAME correlation id; (b) a SCHEDULED (non-request-triggered) run generates its
   OWN fresh correlation id distinct from any prior request.
6. Confirm `docs/BUILD_LOG.md`/`docs/ARTIFACTS.md` accurately reflect the final Hangfire setup
   location and the correlation-id-passing convention for ANY future background job.

## 5. HARD CONSTRAINTS

- The FRAGILE "scan job args for any GUID-looking string" approach is explicitly
  REJECTED in favor of the explicit-named-parameter convention - every future background job in
  this codebase must accept its correlation id as an explicit method parameter, never rely on
  implicit argument scanning.
- A manually-triggered (`run-now`) job carries the TRIGGERING REQUEST's correlation id; a
  SCHEDULED (cron) run generates a fresh one of its own, since there is no triggering request to
  inherit from.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ReconciliationCorrelationTests
dotnet test backend/tests/StoreOS.IntegrationTests
# Expected: new tests pass, full suite remains green after the ReconciliationJob signature
# change (RunAsync now requires an explicit correlationId parameter - update any other caller,
# including the P-905 tests, accordingly).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-906 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-906 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-906` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the finalized `HangfireSetup.cs` location, `ReconciliationJob.RunAsync`'s
new signature (with explicit `correlationId` parameter), the `POST
/api/platform/reconciliation/run-now` route, and the explicit-parameter correlation-id
convention every future background job must follow.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-906: <one-line summary>"` only after verification is
   fully green.
