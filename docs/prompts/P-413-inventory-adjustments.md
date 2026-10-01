# P-413 — Inventory adjustments endpoint (live-permission gated)

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-413** of the StoreOS Cloud build ("Inventory adjustments endpoint (live-permission gated)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-406 (`IInventoryStockService`), P-308 (`IHighImpactOperation`/
  `LiveDbPermissionBehavior`), P-116 (`IAuditWriter`) all complete.
- No manual stock-adjustment endpoint exists yet (e.g. correcting a stock-count discrepancy
  found during a physical audit).

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Inventory/Adjustments/AdjustStockCommand.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Adjustments;

public record AdjustStockCommand(Guid ProductId, int QtyDelta, string Reason)
    : IRequest<AdjustStockResult>, IHighImpactOperation
{
    public string RequiredPermission => "inventory:adjustments";
}

public record AdjustStockResult(int NewStockQty);
```

`AdjustStockValidator.cs`: `QtyDelta != 0` (a zero adjustment is meaningless); `Reason`
`NotEmpty()` (mandatory justification for the audit trail).

`AdjustStockHandler.cs`: for a POSITIVE delta, call `IInventoryStockService.IncreaseStockAsync`
with `movementType = MovementType.Adjustment`; for a NEGATIVE delta, call
`ReserveAndDecrementStockAsync` with the ABSOLUTE VALUE of the delta and the same movement
type (which will correctly throw `insufficient_stock` if the adjustment would take stock
negative — adjustments respect the "never negative" invariant just like any other stock
movement). Write an `IAuditWriter` entry recording the `Reason` text in the `changes` payload.
Use `refType = "Adjustment"`, `refId = Guid.NewGuid().ToString()` (a fresh correlation id for
this specific adjustment event, since there's no separate "Adjustment" entity/table — the
`inventory_movements` row itself IS the adjustment record).

```csharp
public class AdjustStockHandler : IRequestHandler<AdjustStockCommand, AdjustStockResult>
{
    private readonly IInventoryStockService _stockService;
    private readonly IBranchContext _branchContext;
    private readonly IAuditWriter _audit;
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;

    public AdjustStockHandler(IInventoryStockService stockService, IBranchContext branchContext,
        IAuditWriter audit, StoreOsDbContext db, IAccessScope scope)
    { _stockService = stockService; _branchContext = branchContext; _audit = audit; _db = db; _scope = scope; }

    public async Task<AdjustStockResult> Handle(AdjustStockCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value;
        var refId = Guid.NewGuid().ToString();

        if (request.QtyDelta > 0)
            await _stockService.IncreaseStockAsync(branchId, request.ProductId, request.QtyDelta,
                null, MovementType.Adjustment, "Adjustment", refId, ct);
        else
            await _stockService.ReserveAndDecrementStockAsync(branchId, request.ProductId,
                -request.QtyDelta, MovementType.Adjustment, "Adjustment", refId, ct);

        await _audit.WriteAsync(branchId, "inventory.adjusted", "Product", request.ProductId.ToString(),
            new { request.QtyDelta, request.Reason }, ct);

        await _db.SaveChangesAsync(ct);

        var newQty = await _db.Set<BranchInventory>()
            .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == request.ProductId)
            .Select(bi => bi.StockQty).FirstAsync(ct);
        return new AdjustStockResult(newQty);
    }
}
```

Add `POST /api/inventory/adjustments` to `InventoryController.cs`.

## 4. TASK

1. Create `AdjustStockCommand.cs`, `AdjustStockValidator.cs`, `AdjustStockHandler.cs`
   exactly as shown in `StoreOS.Application/Inventory/Adjustments/`.
2. Add the `POST /api/inventory/adjustments` route to `InventoryController.cs`.
3. Write `StoreOS.IntegrationTests/Inventory/AdjustStockTests.cs`: (a) positive adjustment
   increases stock and writes an `Adjustment` movement + audit log entry with the reason text
   preserved; (b) negative adjustment decreases stock correctly; (c) a negative adjustment
   exceeding current stock -> `insufficient_stock`; (d) missing/empty `Reason` -> 400; (e) a
   caller WITHOUT `inventory:adjustments` -> 403 (via live-DB check, confirm this is NOT
   affected by the 30-second token-version cache the same way `TokenVersionBehaviorTests`
   demonstrated — i.e. revoke the permission mid-session and confirm IMMEDIATE rejection using
   the same pattern as P-308's test).

## 5. HARD CONSTRAINTS

- `AdjustStockCommand` implements `IHighImpactOperation`, NOT
  `IRequiresPermission` — adjustments can mask theft/shrinkage and must get the live,
  zero-cache-lag permission check.
- Every adjustment MUST have a non-empty `Reason`, permanently recorded in the audit log —
  this is the ONLY record of WHY stock was manually changed, since there's no separate
  "Adjustment" entity/table, just the `inventory_movements` row plus this audit entry.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter AdjustStockTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-413 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-413 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-413` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `AdjustStockCommand`/`AdjustStockResult` shapes and the
`POST /api/inventory/adjustments` route.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-413: <one-line summary>"` only after verification is
   fully green.
