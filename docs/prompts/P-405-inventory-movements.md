# P-405 — inventory_movements append-only ledger entity+migration

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-405** of the StoreOS Cloud build ("inventory_movements append-only ledger entity+migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-403, P-404 complete: `branch_inventory` exists with query endpoints.
- No append-only movement ledger exists yet. Per Plan §8.6/§8.9, `branch_inventory.stock_qty`
  must be a TRANSACTIONALLY CONSISTENT PROJECTION of the sum of movements — this table is the
  source of truth for reconciliation (P-905).

## 3. CONTEXT BLOCK

Target DDL (Plan §8.6):

```sql
CREATE TABLE inventory_movements (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  imei_id uuid NULL,
  movement_type text NOT NULL,
  qty_delta int NOT NULL CHECK (qty_delta <> 0),
  unit_cost numeric(18,2) NOT NULL,
  ref_type text NOT NULL,
  ref_id text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  created_by uuid NULL
);
CREATE INDEX ix_movements_tenant_branch_product ON inventory_movements (tenant_id, branch_id, product_id);
```

Create `backend/src/StoreOS.Domain/Inventory/MovementType.cs`:

```csharp
namespace StoreOS.Domain.Inventory;

public static class MovementType
{
    public const string Purchase = "Purchase";
    public const string Sale = "Sale";
    public const string SaleVoid = "SaleVoid";
    public const string SaleRefund = "SaleRefund";
    public const string SupplierReturn = "SupplierReturn";
    public const string RepairConsumed = "RepairConsumed";
    public const string RepairPartReturned = "RepairPartReturned";
    public const string Adjustment = "Adjustment";
    public const string TransferOut = "TransferOut";
    public const string TransferIn = "TransferIn";
    public static readonly IReadOnlyList<string> All = new[]
    {
        Purchase, Sale, SaleVoid, SaleRefund, SupplierReturn, RepairConsumed,
        RepairPartReturned, Adjustment, TransferOut, TransferIn
    };
}
```

Create `backend/src/StoreOS.Domain/Inventory/InventoryMovement.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public class InventoryMovement
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ImeiId { get; private set; }
    public string MovementType { get; private set; } = default!;
    public int QtyDelta { get; private set; }
    public decimal UnitCost { get; private set; }
    public string RefType { get; private set; } = default!;
    public string RefId { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private InventoryMovement() { }

    public static InventoryMovement Create(Guid tenantId, Guid branchId, Guid productId, Guid? imeiId,
        string movementType, int qtyDelta, decimal unitCost, string refType, string refId, Guid? createdBy)
    {
        if (!Inventory.MovementType.All.Contains(movementType))
            throw new DomainValidationException("invalid_movement_type", $"Unknown movement type '{movementType}'.");
        if (qtyDelta == 0)
            throw new DomainValidationException("invalid_qty_delta", "Quantity delta cannot be zero.");
        if (string.IsNullOrWhiteSpace(refType)) throw new ArgumentException("RefType is required.", nameof(refType));
        if (string.IsNullOrWhiteSpace(refId)) throw new ArgumentException("RefId is required.", nameof(refId));
        return new InventoryMovement
        {
            TenantId = tenantId, BranchId = branchId, ProductId = productId, ImeiId = imeiId,
            MovementType = movementType, QtyDelta = qtyDelta, UnitCost = unitCost,
            RefType = refType, RefId = refId, CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

(Note the self-referential-looking `Inventory.MovementType.All` in the static factory is just
because the containing class is also named after its own namespace segment — write it simply
as `MovementType.All` since both are already in the same `StoreOS.Domain.Inventory`
namespace; remove the redundant qualifier.)

Apply the Archetype B RLS policy to `inventory_movements`.

## 4. TASK

1. Create `MovementType.cs` and `InventoryMovement.cs` (with the namespace-qualifier
   fix noted) exactly as shown, in `StoreOS.Domain/Inventory/`.
2. Create the EF configuration: `Id` as `ValueGeneratedOnAdd()` matching
   `GENERATED ALWAYS AS IDENTITY`, snake_case columns, the CHECK constraint `qty_delta <> 0`
   (raw SQL), and the named index.
3. Add `DbSet<InventoryMovement> InventoryMovements` to `StoreOsDbContext`, add its
   branch-scoped query filter to `QueryFilters.ApplyAll`.
4. Generate migration `011_InventoryMovements` including the RLS policy, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Inventory/InventoryMovementTests.cs`: (a) valid creation with
   each of the 10 movement types succeeds; (b) an unknown movement type string throws
   `invalid_movement_type`; (c) `qtyDelta == 0` throws `invalid_qty_delta`; (d) both positive
   (e.g. Purchase, +5) and negative (e.g. Sale, -1) deltas are accepted as long as non-zero.

## 5. HARD CONSTRAINTS

- This table is APPEND-ONLY at the application layer — no handler in
  this entire codebase may ever UPDATE or DELETE a row here, only INSERT. Every later prompt
  that changes stock (purchases P-410, checkout P-510, voids P-524, refunds P-525, supplier
  returns P-608, repair parts P-705, transfers P-414, adjustments P-413) must write a NEW
  `InventoryMovement` row reflecting the change, never edit history.
- `qty_delta <> 0` is enforced at both domain and DB (CHECK) level.

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
dotnet ef migrations add 011_InventoryMovements --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter InventoryMovementTests
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
   `BLOCKED: P-405 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-405 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-405` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `MovementType` constant list (all 10 values) and `InventoryMovement`
signature, migration name `011_InventoryMovements`, and the append-only-forever rule.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-405: <one-line summary>"` only after verification is
   fully green.
