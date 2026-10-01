# P-703 — Repair job CRUD + intake/diagnosis/commercial fields endpoints

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-703** of the StoreOS Cloud build ("Repair job CRUD + intake/diagnosis/commercial fields endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-702 complete: `RepairJob` entity/table/numbering exist.
- No REST endpoints exist yet (Requirements Section 21).

## 3. CONTEXT BLOCK

Create vertical slices under `StoreOS.Application/Repairs/Jobs/`:

`CreateRepairJob` (`IRequiresPermission`, `repairs:manage`): takes all intake fields
(customer id/name/phone/address, device brand/model/type/serial/imei1/imei2, condition,
accessories, problem description, customer notes). Handler allocates a job number via
`InvoiceSequenceAllocator` + `RepairJobNumberFormatter` (P-702), calls `RepairJob.Intake(...)`.

`GetRepairJobs` (paginated list, filters: `status` (Requirements Section 22.4: "All", "Active",
individual statuses), `search` matching job number/customer name/phone/device model/serial/
imei1 per Requirements Section 22.3): no special permission (technicians need read access too).

`GetRepairJobById`: full detail view.

`UpdateRepairJobDiagnosis` (`IRequiresPermission`, `repairs:manage` — or should technicians be
allowed? Requirements Section 2's Repair Technician role explicitly lists "Record diagnosis" as
an allowed action; add a technician-appropriate permission path: since `PermissionCatalog`
(P-115) only has `repairs:manage` as a single catalog entry per Plan Section 6.2's table, and
the master prompt's manifest coverage checklist notes any DISCREPANCY between a needed
permission and the fixed 17-code catalog should be resolved by using the closest existing code
— use `repairs:manage` for ALL repair-related mutations uniformly, including diagnosis/notes
updates by technicians; a tenant wanting finer-grained technician-vs-manager separation can
create a "Technician" ROLE that is ALSO granted `repairs:manage` alongside other roles, since
permissions gate ACTIONS while ROLES aggregate permissions per-user — this achieves the desired
UX without needing a new permission code).

`UpdateRepairJobCommercials` (`IRequiresPermission`, `repairs:manage`): calls
`SetCommercials(...)`.

Create `backend/src/StoreOS.Api/Controllers/RepairJobsController.cs`:
`POST/GET /api/repair-jobs`, `GET/PATCH /api/repair-jobs/{id}`,
`PATCH /api/repair-jobs/{id}/diagnosis`, `PATCH /api/repair-jobs/{id}/commercials`.

## 4. TASK

1. Create the five vertical slices under `StoreOS.Application/Repairs/Jobs/` exactly
   as described, all gated by `repairs:manage` except `GetRepairJobs`/`GetRepairJobById` (no
   special permission).
2. Create `RepairJobsController.cs` with the five routes.
3. Write `StoreOS.IntegrationTests/Repairs/RepairJobsCrudTests.cs`: (a) creating a job allocates
   a correctly-formatted, unique job number and sets `Status=Received`; (b) search by device
   model, by IMEI1, by customer phone all work; (c) status filter `Active` returns jobs NOT in
   `Delivered`/`Cancelled` (reusing `RepairStatus.IsActive`); (d) updating diagnosis/commercials
   persists correctly and `BalanceDue` recomputes; (e) a caller without `repairs:manage` -> 403
   on mutations, but CAN still read via the list/detail queries.

## 5. HARD CONSTRAINTS

- All repair job MUTATIONS use `repairs:manage` uniformly — there is
  no separate "technician" permission code in the fixed 17-code catalog (P-115); role
  composition (assigning `repairs:manage` to a "Technician" role alongside a narrower branch
  scope) is how a tenant achieves finer-grained access control, not a new permission code.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RepairJobsCrudTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-703 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-703 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-703` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: all five vertical-slice shapes and the `RepairJobsController` routes, and the
documented decision to use `repairs:manage` uniformly for all repair mutations.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-703: <one-line summary>"` only after verification is
   fully green.
