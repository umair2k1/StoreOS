# P-1117 — Repairs: service catalogue + job board (kanban by status)

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1117** of the StoreOS Cloud build ("Repairs: service catalogue + job board (kanban by status)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1116 complete: liabilities screen works.
- GET/POST/PATCH /api/repair-services (P-701), GET/POST /api/repair-jobs,
  POST /api/repair-jobs/{id}/status (P-703/704) all exist, gated by the repairs feature flag
  (P-708) - a disabled tenant gets 422 repairs_module_disabled from every one of these.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/repairs/services/repair-services.component.ts` (simple
CRUD list/form for service definitions, showing the SAC/GST/duration/warranty defaults from
P-701). Create `frontend/src/app/features/repairs/job-board.component.ts`: a KANBAN-style board
with one column per status (Received, Diagnosing, WaitingForApproval, WaitingForParts, InRepair,
ReadyForPickup - Delivered/Cancelled shown in a collapsed "closed" section, not full columns,
per Requirements Section 22.4's Active/individual-status distinction), where dragging a job card
between columns calls `POST /api/repair-jobs/{id}/status` with the target status, showing an
error toast (and reverting the card's position) if the backend rejects an illegal transition
(P-704's `RepairStatusTransitions` guard). Both screens check the repairs feature flag first
(via a small `GET /api/settings/repairs-enabled` check, P-708) and show a clear
"Repairs module is not enabled" message with a link to Settings if disabled, rather than a
confusing broken UI.

## 4. TASK

1. Create `RepairServicesComponent` (CRUD list/form).
2. Create `JobBoardComponent` with drag-and-drop status columns (use Angular CDK's DragDrop
   module, already added in P-1011), calling the status-change endpoint on drop and reverting on
   rejection.
3. Add the feature-flag pre-check to both screens' route resolvers or component init logic.
4. Write `.spec.ts` for both: (a) service CRUD works with correct defaults displayed; (b) dragging
   a card to an illegal target column triggers the revert-and-error-toast behavior (mock a 422
   response); (c) a legal drag-drop calls the endpoint and the card stays in its new column; (d)
   with the feature flag disabled (mocked), both screens show the disabled message instead of
   their normal content.

## 5. HARD CONSTRAINTS

- An illegal drag-and-drop status transition must visually REVERT the card to its original
  column and show a clear error, never leave the UI in a state inconsistent with the server's
  actual data.
- Both screens must gracefully handle the repairs-disabled state with a clear message, never a
  confusing broken/empty UI.

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
cd store-os-cloud/frontend
ng test --include='**/repair-services.component.spec.ts' --include='**/job-board.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1117 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1117 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1117` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RepairServicesComponent`/`JobBoardComponent` structures and the feature-flag
pre-check pattern.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1117: <one-line summary>"` only after verification is
   fully green.
