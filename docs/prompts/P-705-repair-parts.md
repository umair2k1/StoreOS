# P-705 — repair_job_parts entity/migration + reserve/consume/return via IInventoryStockService

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-705** of the StoreOS Cloud build ("repair_job_parts entity/migration + reserve/consume/return via IInventoryStockService") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-704 complete: status transitions with full audit history work.
- No repair-parts-consumption schema exists yet (Requirements Section 22.5, Plan Section 8.8).
  `IInventoryStockService` (P-406/408) is fully available for reuse.

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.8):

```sql
CREATE TABLE repair_job_parts (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  repair_job_id uuid NOT NULL REFERENCES repair_jobs(id),
  product_id uuid NULL,
  part_name text NOT NULL,
  quantity int NOT NULL CHECK (quantity > 0),
  unit_cost numeric(18,2) NOT NULL,
  charge_amount numeric(18,2) NOT NULL,
  status text NOT NULL CHECK (status IN ('Reserved','Consumed','Returned')),
  created_at timestamptz NOT NULL DEFAULT now()
);
```

Create `backend/src/StoreOS.Domain/Repairs/RepairPartStatus.cs`
(`Reserved`/`Consumed`/`Returned` + `All`).

Create `backend/src/StoreOS.Domain/Repairs/RepairJobPart.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Repairs;

public class RepairJobPart
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RepairJobId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string PartName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal ChargeAmount { get; private set; }
    public string Status { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private RepairJobPart() { }

    public static RepairJobPart Reserve(Guid tenantId, Guid repairJobId, Guid? productId,
        string partName, int quantity, decimal unitCost, decimal chargeAmount)
    {
        if (quantity <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");
        if (string.IsNullOrWhiteSpace(partName)) throw new DomainValidationException("part_name_required", "Part name is required.");
        return new RepairJobPart
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RepairJobId = repairJobId, ProductId = productId,
            PartName = partName.Trim(), Quantity = quantity, UnitCost = unitCost, ChargeAmount = chargeAmount,
            Status = RepairPartStatus.Reserved, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkConsumed()
    {
        if (Status != RepairPartStatus.Reserved)
            throw new DomainValidationException("part_not_reserved", "Only a Reserved part can be consumed.");
        Status = RepairPartStatus.Consumed;
    }

    public void MarkReturned()
    {
        if (Status == RepairPartStatus.Returned)
            throw new DomainValidationException("part_already_returned", "Part has already been returned.");
        Status = RepairPartStatus.Returned;
    }
}
```

Create the vertical slices under `StoreOS.Application/Repairs/Parts/`:

`ReservePart/ReservePartCommand.cs` (`IRequiresPermission`, `repairs:manage`): creates a
`RepairJobPart` in `Reserved` status. If `ProductId` is provided (a tracked inventory item, not
a generic/untracked part), this reservation does NOT yet decrement stock (Requirements Section
22.5 part statuses imply Reserved is a soft hold; actual decrement happens on CONSUME per
Requirements Section 22.5 "Physical inventory must be adjusted appropriately when parts are
consumed or returned" — note it says consumed/returned, not reserved).

`ConsumePart/ConsumePartCommand.cs` (`IRequiresPermission`, `repairs:manage`): loads the
`RepairJobPart`, if it has a `ProductId`, calls
`IInventoryStockService.ReserveAndDecrementStockAsync(branchId, productId, quantity,
MovementType.RepairConsumed, "RepairJobPart", partId, ct)` (fails atomically with
`insufficient_stock` if not enough stock — Requirements Section 30.3/23's "Prevent negative
stock unless explicitly permitted"), then calls `part.MarkConsumed()`.

`ReturnPart/ReturnPartCommand.cs` (`IRequiresPermission`, `repairs:manage`): if the part has a
`ProductId` AND its status is `Consumed` (returning a part that was actually taken from stock),
calls `IInventoryStockService.IncreaseStockAsync(branchId, productId, quantity, null,
MovementType.RepairPartReturned, "RepairJobPart", partId, ct)` to reverse the consumption, then
`part.MarkReturned()`. If the part was still `Reserved` (never actually consumed), just mark it
`Returned` without any stock movement (nothing was ever decremented).

Add routes to `RepairJobsController.cs`: `POST /api/repair-jobs/{id}/parts` (reserve),
`POST /api/repair-jobs/{id}/parts/{partId}/consume`,
`POST /api/repair-jobs/{id}/parts/{partId}/return`,
`GET /api/repair-jobs/{id}/parts`.

## 4. TASK

1. Create `RepairPartStatus.cs` and `RepairJobPart.cs` exactly as shown in
   `StoreOS.Domain/Repairs/`.
2. Create EF configuration (CHECK on `status`, `quantity > 0`, snake_case, FK to
   `repair_jobs`).
3. Add `DbSet<RepairJobPart>` to `StoreOsDbContext`/`QueryFilters.ApplyAll` (tenant-only,
   same reasoning as `repair_job_status_history`).
4. Generate migration `029_RepairJobParts`... wait, per the manifest's assigned migration
   numbering this should follow `028_RepairStatusHistory` from P-704 — name it
   `030_RepairJobParts` if `029` is reserved elsewhere in your actual running migration
   sequence; use whatever the NEXT actual sequential migration number is in your real repo at
   this point (check `docs/ARTIFACTS.md`'s migration ledger and continue from there,
   regardless of what number this prompt's title suggests) — apply it, and re-run
   `RlsCoverageTests`.
5. Create the three vertical slices (`ReservePart`, `ConsumePart`, `ReturnPart`) plus a
   `GetRepairJobParts` query, exactly as described.
6. Add the four routes to `RepairJobsController.cs`.
7. Write `StoreOS.IntegrationTests/Repairs/RepairPartsTests.cs`: (a) reserving a tracked part
   does NOT decrement stock yet; (b) consuming it DOES decrement stock atomically, and fails
   with `insufficient_stock` if not enough is available (whole operation rolls back, part stays
   `Reserved`); (c) returning a `Consumed` part correctly INCREASES stock back; (d) returning a
   still-`Reserved` part (never consumed) does NOT create any stock movement, just marks it
   `Returned`; (e) an UNTRACKED part (`ProductId = null`, e.g. a generic screw or adhesive not
   in the catalog) can be reserved/consumed/returned without ANY stock service interaction at
   all (purely a cost/charge record on the repair job); (f) attempting to consume an
   already-`Consumed` part -> `part_not_reserved`.

## 5. HARD CONSTRAINTS

- Stock decrements happen on CONSUME, not on RESERVE — this
  distinction is explicit and load-bearing per Requirements Section 22.5's phrasing.
- An untracked part (`ProductId == null`) NEVER touches `IInventoryStockService` at any stage
  of its lifecycle — it exists purely as a cost/billing line on the repair job.
- Use whatever the ACTUAL next sequential migration number is in your real repository at this
  point — do not blindly assume a specific number from this prompt's own title if your running
  ledger has drifted (e.g. due to earlier prompts needing extra migrations).

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
dotnet ef migrations add <NextSequentialName>_RepairJobParts --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter RepairPartsTests
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
   `BLOCKED: P-705 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-705 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-705` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RepairJobPart`/`RepairPartStatus` signatures, the ACTUAL migration name
used (report the real number), the three command shapes, and confirmation stock only moves on
Consume/Return, never on Reserve, and never at all for untracked parts.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-705: <one-line summary>"` only after verification is
   fully green.
