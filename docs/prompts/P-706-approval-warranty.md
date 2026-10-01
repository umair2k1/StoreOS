# P-706 — Customer approval + warranty fields endpoints

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-706** of the StoreOS Cloud build ("Customer approval + warranty fields endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-705 complete: parts reservation/consumption/return fully work.
- RepairJob (P-702) already has RecordApproval(approvalState, timestamp) and Deliver
  (deliveredAt, warrantyUntil) methods defined, but no REST endpoints expose them
  specifically (they were designed into the entity in P-702 but not yet wired to routes).

## 3. CONTEXT BLOCK

Create the vertical slice `StoreOS.Application/Repairs/Approval/RecordApprovalCommand.cs`
(`IRequiresPermission`, `repairs:manage`):

```csharp
public record RecordApprovalCommand(Guid RepairJobId, string ApprovalState)
    : IRequest, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
```

`ApprovalState` is a simple small-enum field (Requirements Section 21.7 just says "Approval
state" without enumerating fixed values) - validate it's one of a small documented set for
consistency: `"Pending"`, `"Approved"`, `"Rejected"` (define these as constants in a small
`ApprovalState.cs` in `StoreOS.Domain/Repairs/`). Handler calls
`job.RecordApproval(request.ApprovalState, DateTimeOffset.UtcNow)`.

Create `StoreOS.Application/Repairs/Deliver/DeliverRepairJobCommand.cs`
(`IRequiresPermission`, `repairs:manage`):

```csharp
public record DeliverRepairJobCommand(Guid RepairJobId, int? WarrantyDaysOverride) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
```

Handler: computes `warrantyUntil = DateOnly.FromDateTime(DateTime.UtcNow).AddDays
(request.WarrantyDaysOverride ?? DEFAULT_WARRANTY_DAYS)`, where `DEFAULT_WARRANTY_DAYS` is
resolved from the underlying repair SERVICE product's `WarrantyDays` if this job is linked to
one (Requirements Section 21.9: "based on the configured service/job warranty policy") - if the
job has no single linked service (e.g. multiple services/parts), fall back to the tenant-wide
default of 30 days (Requirements Section 20.1's stated default). Calls
`job.TransitionTo(RepairStatus.Delivered)` (reusing P-704's guarded transition, which ALSO
already sets `DeliveredAt`) then `job.Deliver(DateTimeOffset.UtcNow, warrantyUntil)` - reconcile
whichever of these two methods actually ends up setting `DeliveredAt`/`WarrantyUntil` based on
your ACTUAL P-702/P-704 implementations (avoid double-setting or conflicting logic; adjust one
or the other method to not duplicate work).

Add `POST /api/repair-jobs/{id}/approval`, `POST /api/repair-jobs/{id}/deliver` to
`RepairJobsController.cs`.

## 4. TASK

1. Create `ApprovalState.cs` (constants) in `StoreOS.Domain/Repairs/`.
2. Reconcile `RepairJob.RecordApproval`/`Deliver`/`TransitionTo` against their actual P-702/
   P-704 implementations, fixing any double-work or conflict between `TransitionTo` and
   `Deliver` regarding `DeliveredAt`.
3. Create `RecordApprovalCommand.cs`/handler and `DeliverRepairJobCommand.cs`/handler exactly
   as described, in their respective vertical-slice folders.
4. Add both routes to `RepairJobsController.cs`.
5. Write `StoreOS.IntegrationTests/Repairs/ApprovalAndDeliveryTests.cs`: (a) recording an
   invalid `ApprovalState` string -> 400; a valid one persists correctly with a timestamp; (b)
   delivering a job NOT in `ReadyForPickup` -> `illegal_status_transition` (reusing P-704's
   guard); (c) delivering a job correctly linked to a repair-service product with
   `WarrantyDays=45` -> `WarrantyUntil` is exactly 45 days from delivery; (d) delivering a job
   with NO override and no clearly identifiable single service -> falls back to the 30-day
   tenant default; (e) `DeliveredAt` is set exactly once, correctly, with no conflicting
   double-write between `TransitionTo` and `Deliver`.

## 5. HARD CONSTRAINTS

- Resolve the potential double-responsibility between
  `RepairJob.TransitionTo` (which may already set `DeliveredAt` per P-704's own description)
  and `RepairJob.Deliver` (which ALSO sets timestamps per P-702's description) - pick ONE
  method as the authoritative setter for `DeliveredAt`/`WarrantyUntil` and have the OTHER defer
  to it, avoiding any inconsistent or duplicated state mutation.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ApprovalAndDeliveryTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-706 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-706 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-706` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ApprovalState` constants, both command shapes, the two new
`RepairJobsController` routes, and which method (`TransitionTo` or `Deliver`) is now the
authoritative setter for `DeliveredAt`/`WarrantyUntil`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-706: <one-line summary>"` only after verification is
   fully green.
