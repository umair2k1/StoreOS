# P-704 — repair_job_status_history entity/migration + POST /repair-jobs/{id}/status (state machine)

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-704** of the StoreOS Cloud build ("repair_job_status_history entity/migration + POST /repair-jobs/{id}/status (state machine)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-703 complete: repair job CRUD works, `Status` has no public mutator yet.
- No status-history table exists yet (Requirements Section 22.1, Plan Section 8.8).

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.8):

```sql
CREATE TABLE repair_job_status_history (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  repair_job_id uuid NOT NULL REFERENCES repair_jobs(id),
  from_status text, to_status text NOT NULL,
  notes text, changed_by uuid, changed_at timestamptz NOT NULL DEFAULT now()
);
```

Create `backend/src/StoreOS.Domain/Repairs/RepairJobStatusHistory.cs` (append-only, immutable,
same style as `InventoryMovement`/`CustomerPayment` — no update method ever):

```csharp
public static RepairJobStatusHistory Create(Guid tenantId, Guid repairJobId, string? fromStatus,
    string toStatus, string? notes, Guid? changedBy)
```

Define the LEGAL STATUS TRANSITION graph explicitly (Requirements Section 22 lists the 8
statuses in a roughly linear order but does not mandate a strict linear-only state machine —
real repair shops need flexibility, e.g. going from `WaitingForParts` back to `Diagnosing` if a
part turns out wrong). Implement a PERMISSIVE graph that forbids only clearly illogical
transitions: FROM `Delivered` or `Cancelled`, NO further transitions are ever legal (terminal
states); TO `Delivered`, only legal FROM `ReadyForPickup`; every OTHER transition between the
6 non-terminal, non-`Delivered`-target statuses (`Received`, `Diagnosing`,
`WaitingForApproval`, `WaitingForParts`, `InRepair`, `ReadyForPickup`) is permitted in either
direction, PLUS any of those 6 can transition directly to `Cancelled`. Encode this as a small
static rule table in `StoreOS.Domain/Repairs/RepairStatusTransitions.cs`:

```csharp
namespace StoreOS.Domain.Repairs;

public static class RepairStatusTransitions
{
    private static readonly HashSet<string> Terminal = new() { RepairStatus.Delivered, RepairStatus.Cancelled };
    private static readonly HashSet<string> NonTerminal = new() {
        RepairStatus.Received, RepairStatus.Diagnosing, RepairStatus.WaitingForApproval,
        RepairStatus.WaitingForParts, RepairStatus.InRepair, RepairStatus.ReadyForPickup
    };

    public static bool IsLegal(string from, string to)
    {
        if (Terminal.Contains(from)) return false; // terminal states never transition further
        if (to == RepairStatus.Delivered) return from == RepairStatus.ReadyForPickup;
        if (to == RepairStatus.Cancelled) return NonTerminal.Contains(from);
        return NonTerminal.Contains(from) && NonTerminal.Contains(to) && from != to;
    }
}
```

Add `TransitionTo(string newStatus)` to `RepairJob.cs`:

```csharp
public void TransitionTo(string newStatus)
{
    if (!RepairStatusTransitions.IsLegal(Status, newStatus))
        throw new StoreOS.Domain.Exceptions.DomainValidationException("illegal_status_transition",
            $"Cannot transition repair job from '{Status}' to '{newStatus}'.");
    Status = newStatus;
    if (newStatus == RepairStatus.Delivered) DeliveredAt = DateTimeOffset.UtcNow;
}
```

Create the vertical slice `ChangeStatus/ChangeRepairStatusCommand.cs`
(`IRequiresPermission`, `repairs:manage`): takes `RepairJobId`, `NewStatus`, `Notes`. Handler
loads the job, captures `fromStatus = job.Status`, calls `job.TransitionTo(newStatus)` (throws
if illegal), creates a `RepairJobStatusHistory` row recording BOTH `fromStatus` and `toStatus`,
saves atomically.

Add `POST /api/repair-jobs/{id}/status` to `RepairJobsController.cs`. Add
`GET /api/repair-jobs/{id}/status-history` for viewing the audit trail.

## 4. TASK

1. Create `RepairJobStatusHistory.cs` and `RepairStatusTransitions.cs` in
   `StoreOS.Domain/Repairs/`, add `TransitionTo` to `RepairJob.cs`.
2. Create EF configuration for the history table: `Id` as identity, snake_case columns, FK to
   `repair_jobs`.
3. Add `DbSet<RepairJobStatusHistory>` to `StoreOsDbContext`/`QueryFilters.ApplyAll`
   (branch-scoped via join reasoning similar to `sale_items` — it has `tenant_id` directly per
   the DDL but no `branch_id`; use tenant-only filter, relying on the parent `RepairJob` load
   being branch-filtered).
4. Generate migration `028_RepairStatusHistory` including RLS, apply it.
5. Re-run `RlsCoverageTests`.
6. Create `ChangeRepairStatusCommand.cs`/handler and `GetRepairStatusHistory` query, add both
   routes to `RepairJobsController.cs`.
7. Write `StoreOS.Domain.Tests/Repairs/RepairStatusTransitionsTests.cs`: every legal transition
   from the described graph passes `IsLegal`; every illegal one (e.g. `Delivered ->
   InRepair`, `Received -> Delivered` directly, `Cancelled -> Received`) fails.
8. Write `StoreOS.IntegrationTests/Repairs/ChangeRepairStatusTests.cs`: (a) a legal transition
   succeeds, creates exactly one history row with correct from/to values; (b) an illegal
   transition -> `illegal_status_transition`, NO history row created (atomic failure); (c) a
   job that reaches `Delivered` correctly sets `DeliveredAt`; (d) multiple sequential legal
   transitions create a correctly ordered, complete history trail.

## 5. HARD CONSTRAINTS

- `RepairJobStatusHistory` is append-only forever — no update method,
  ever.
- The transition graph is intentionally PERMISSIVE among non-terminal states (real repair
  workflows are not strictly linear) but STRICT about terminal states (`Delivered`/`Cancelled`
  never transition further) and about `Delivered` being reachable ONLY from
  `ReadyForPickup` — do not loosen or tighten this specific graph without updating both the
  code and this documented rationale together.

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
dotnet ef migrations add 028_RepairStatusHistory --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter RepairStatusTransitionsTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter ChangeRepairStatusTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-704 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-704 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-704` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RepairJobStatusHistory`/`RepairStatusTransitions` signatures, the full
legal-transition graph rules, migration name `028_RepairStatusHistory`, and the two new
`RepairJobsController` routes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-704: <one-line summary>"` only after verification is
   fully green.
