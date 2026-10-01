# P-701 — repair_services entity/migration + CRUD (defaults SAC/GST/duration/warranty)

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-701** of the StoreOS Cloud build ("repair_services entity/migration + CRUD (defaults SAC/GST/duration/warranty)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 6 (Customers/Suppliers/Expenses/Liabilities, P-601-611) fully complete.
- Recall from P-401: `products.category = 'RepairService'` ALREADY models repair SERVICE
  DEFINITIONS as catalog entries with SAC/GST/duration/warranty defaults, per that prompt's
  design. Requirements Section 20 describes a `repair_services` table SEPARATELY in the DDD
  spec (Section 29.16). Resolve this apparent duplication explicitly: Plan Section 8.4 already
  states "Repair services live in the same catalog with category RepairService... they simply
  never get branch_inventory rows" — meaning the PLAN's chosen architecture treats
  `products` (with `category='RepairService'`) AS the repair-service catalog, NOT a separate
  `repair_services` table. Follow the PLAN (per the master prompt's rule: "Implementation Plan
  wins on architecture"). This prompt therefore does NOT create a new `repair_services` table
  — it EXTENDS the existing `Product`/`products` entity (P-401) with any remaining
  repair-specific fields not yet present, and builds repair-service-specific CRUD/query
  endpoints as a thin, semantically clear layer over the SAME underlying `products` table
  filtered to `category = 'RepairService'`.

## 3. CONTEXT BLOCK

Review `Product.cs` (P-401) — it already has `EstimatedDurationMinutes`,
`WarrantyDays`, `HsnCode` (used as SAC for services, since Requirements Section 20.1's "SAC
code" and Section 11's "HSN code" are the same underlying GST classification concept just
named differently for goods vs. services), `GstRate`, `IsTaxable`, `IsActive`, defaulted
correctly in `Product.Create` for `RepairService` category (SAC `998713`, GST 18%, duration 60
min, warranty 30 days — already implemented in P-401). The ONLY gap: Requirements Section 20.1
lists `Description` (already present) — confirm no other Section 20.1 field is missing from
`Product`. On review, all fields ARE already present. This prompt therefore is PURELY an API
surface addition: repair-service-specific endpoints for a clearer frontend contract, without
any new backend entity or migration.

Create `backend/src/StoreOS.Application/Repairs/Services/CreateRepairService/CreateRepairServiceCommand.cs`
(`IRequiresPermission`, `repairs:manage`):

```csharp
public record CreateRepairServiceCommand(string Name, string? Description, decimal StandardCharge,
    bool Taxable, decimal? GstRate, string? SacCode, int? EstimatedDurationMinutes, int? WarrantyDays)
    : IRequest<Guid>, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}
```

Handler calls `Product.Create(tenantId, name, ProductCategory.RepairService, standardCharge,
0m /* no purchase price for a service */, gstRate ?? 18m, sacCode)` then
`UpdateDescriptionAndServiceFields(...)` (P-401's existing method) to apply
duration/warranty/description, relying on `Product.Create`'s ALREADY-IMPLEMENTED
RepairService-category defaults for anything left null.

Create `GetRepairServices/GetRepairServicesQuery.cs`
(`IRequest<PagedResult<RepairServiceDto>>`, filtering `products.category ==
ProductCategory.RepairService` and optionally `isActive` — Requirements Section 20.2:
"Inactive services should not normally appear in new ticket/POS selection" — default
`isActive = true` unless explicitly overridden):

```csharp
public record RepairServiceDto(Guid Id, string Name, string? Description, decimal StandardCharge,
    bool Taxable, decimal GstRate, string? SacCode, int? EstimatedDurationMinutes,
    int? WarrantyDays, bool IsActive);
```

Create `UpdateRepairService/UpdateRepairServiceCommand.cs` (`IRequiresPermission`,
`repairs:manage`) and `DeactivateRepairService/DeactivateRepairServiceCommand.cs` (same
permission, calls `Product.Deactivate()` — Requirements Section 20.2: "Historical usage must
remain intact," which is automatically satisfied since `Deactivate()` never deletes the row or
touches any historical `sale_items`/`repair_job_parts` snapshot referencing this product).

Create `backend/src/StoreOS.Api/Controllers/RepairServicesController.cs`:
`POST/GET /api/repair-services`, `PATCH /api/repair-services/{id}`,
`POST /api/repair-services/{id}/deactivate`.

## 4. TASK

1. Confirm (by reviewing the actual `Product.cs` source) that no new field is needed;
   if you find a genuine gap against Requirements Section 20.1's field list, add the missing
   property now via a small migration, documenting the addition explicitly — otherwise proceed
   with zero schema changes.
2. Create the four vertical slices under `StoreOS.Application/Repairs/Services/` exactly as
   described, all operating against the EXISTING `products` table filtered to
   `category = RepairService`.
3. Create `RepairServicesController.cs` with the four routes.
4. Write `StoreOS.IntegrationTests/Repairs/RepairServicesTests.cs`: (a) creating a repair
   service with minimal input (name + charge only) gets ALL the documented defaults (SAC
   998713, GST 18%, duration 60, warranty 30); (b) creating with explicit overrides for each
   field respects them instead of the defaults; (c) `GET /api/repair-services` by default
   excludes inactive services; passing `isActive=false` explicitly includes them; (d)
   deactivating a service that has ALREADY been used in a historical sale line does not affect
   that sale line's snapshot data (seed a sale_item referencing this service product first, then
   deactivate, then re-fetch the sale detail and confirm its snapshot fields are unchanged); (e)
   this endpoint NEVER creates a `branch_inventory` row for the created service (Invariant 3,
   re-verified at this specific new entry point).

## 5. HARD CONSTRAINTS

- This prompt creates ZERO new database tables — repair services are
  `products` rows with `category = 'RepairService'`, per the Plan's explicit architecture
  decision (Plan wins over the Requirements doc's more literal per-table description, per the
  master prompt's conflict-resolution rule, and this exact conflict is recorded in
  `MANIFEST.md`'s conflict-notes section).
- `Product.Create`'s EXISTING RepairService defaulting logic (from P-401) must not be
  duplicated here — this prompt's handler calls the SAME factory method, it doesn't
  reimplement the defaulting rules.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RepairServicesTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-701 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-701 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-701` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: confirmation that repair services are modeled as `products` rows (no new
table), the four vertical-slice shapes, and the `RepairServicesController` routes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-701: <one-line summary>"` only after verification is
   fully green.
