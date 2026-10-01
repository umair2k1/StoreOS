# P-406 — IInventoryStockService: conditional decrement/increment (internal app service contract)

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-406** of the StoreOS Cloud build ("IInventoryStockService: conditional decrement/increment (internal app service contract)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-405 complete: `inventory_movements` append-only ledger exists.
- No CONCURRENCY-SAFE stock mutation service exists yet. This is one of the most important
  prompts in Phase 4 — it implements Plan §5.1's cross-module internal service pattern
  (`IInventoryStockService`) and Plan §9.4's conditional-update concurrency guarantee.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Inventory/IInventoryStockService.cs`:

```csharp
namespace StoreOS.Application.Inventory;

public interface IInventoryStockService
{
    Task ReserveAndDecrementStockAsync(Guid branchId, Guid productId, int qty, string movementType,
        string refType, string refId, CancellationToken ct);

    Task IncreaseStockAsync(Guid branchId, Guid productId, int qty, decimal? newCostPrice,
        string movementType, string refType, string refId, CancellationToken ct);
}
```

Implementation `backend/src/StoreOS.Infrastructure/Inventory/InventoryStockService.cs`. The
CRITICAL piece is the conditional UPDATE (Plan §9.4) executed via raw SQL through
`ExecuteSqlInterpolatedAsync` (parameterized, injection-safe) rather than a naive
EF load-then-save round trip, so the DB's own row-level locking makes the check-and-decrement
atomic even under concurrent requests on different connections within the same transaction
isolation level:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Inventory;

public class InventoryStockService : IInventoryStockService
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    public InventoryStockService(StoreOsDbContext db, IAccessScope scope) { _db = db; _scope = scope; }

    public async Task ReserveAndDecrementStockAsync(Guid branchId, Guid productId, int qty,
        string movementType, string refType, string refId, CancellationToken ct)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");

        var costPrice = await _db.Set<BranchInventory>()
            .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == productId)
            .Select(bi => bi.CostPrice)
            .FirstOrDefaultAsync(ct);

        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE branch_inventory
            SET stock_qty = stock_qty - {qty}
            WHERE tenant_id = {_scope.TenantId} AND branch_id = {branchId} AND product_id = {productId}
              AND stock_qty >= {qty}", ct);

        if (affected == 0)
            throw new DomainValidationException("insufficient_stock",
                "Not enough stock available at this branch for the requested quantity.");

        var movement = InventoryMovement.Create(_scope.TenantId, branchId, productId, null,
            movementType, -qty, costPrice, refType, refId, _scope.IsAuthenticated ? _scope.UserId : null);
        await _db.Set<InventoryMovement>().AddAsync(movement, ct);
    }

    public async Task IncreaseStockAsync(Guid branchId, Guid productId, int qty, decimal? newCostPrice,
        string movementType, string refType, string refId, CancellationToken ct)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");

        var existing = await _db.Set<BranchInventory>()
            .FirstOrDefaultAsync(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId
                && bi.ProductId == productId, ct);

        decimal effectiveCost;
        if (existing is null)
        {
            var row = BranchInventory.Create(_scope.TenantId, branchId, productId);
            row.IncreaseStock(qty, newCostPrice);
            effectiveCost = row.CostPrice;
            await _db.Set<BranchInventory>().AddAsync(row, ct);
        }
        else
        {
            existing.IncreaseStock(qty, newCostPrice);
            effectiveCost = existing.CostPrice;
        }

        var movement = InventoryMovement.Create(_scope.TenantId, branchId, productId, null,
            movementType, qty, effectiveCost, refType, refId, _scope.IsAuthenticated ? _scope.UserId : null);
        await _db.Set<InventoryMovement>().AddAsync(movement, ct);
    }
}
```

Register scoped: `builder.Services.AddScoped<IInventoryStockService, InventoryStockService>();`

Note: NEITHER method calls `SaveChangesAsync()` — per the established convention (P-116's
`AuditWriter`), these internal application services STAGE changes on the ambient
`StoreOsDbContext`; the CALLING handler (e.g. `CheckoutHandler` in P-510) issues ONE
`SaveChangesAsync()` at the end covering the sale, the stock movement, the ledger entry, and
the audit log entry all atomically. The `ExecuteSqlInterpolatedAsync` conditional UPDATE is
the ONE EXCEPTION — it executes IMMEDIATELY against the database (not deferred via change
tracking) since it needs its result (`affected == 0`) evaluated synchronously to decide
whether to throw; this is safe because it runs within the same ambient transaction as
everything else (courtesy of P-109's `TransactionBehavior`), so it still rolls back
correctly if a LATER step in the same request fails.

## 4. TASK

1. Create `IInventoryStockService.cs` in `StoreOS.Application/Inventory/` and
   `InventoryStockService.cs` in `StoreOS.Infrastructure/Inventory/` exactly as shown, register
   scoped.
2. Write `StoreOS.IntegrationTests/Inventory/InventoryStockServiceTests.cs`: (a) increase then
   decrease stock, confirm `branch_inventory.stock_qty` and the two resulting
   `inventory_movements` rows (positive then negative delta) both look correct after
   `SaveChangesAsync()`; (b) attempt to decrement below zero -> throws `insufficient_stock`,
   AND confirm no partial `inventory_movements` row was persisted (the whole operation, though
   only staged, is discarded when the exception propagates and the caller's
   `SaveChangesAsync()` never runs / the ambient transaction rolls back).
3. Write `StoreOS.ConcurrencyTests/InventoryStockConcurrencyTests.cs`: seed a product with
   `stock_qty = 1` at a branch. Launch TWO concurrent tasks, each in its OWN
   `IServiceScope` (simulating two separate concurrent HTTP requests, each with its own
   `StoreOsDbContext`/transaction), both calling `ReserveAndDecrementStockAsync(..., qty: 1,
   ...)` for the SAME product/branch, followed by their own `SaveChangesAsync()`. Assert:
   EXACTLY ONE of the two tasks succeeds; the OTHER throws `insufficient_stock` (or, if it hits
   the conditional UPDATE after the first has already committed, gets `affected == 0`
   naturally); final `stock_qty` is exactly 0, never negative, never double-decremented.

## 5. HARD CONSTRAINTS

- The conditional `UPDATE ... WHERE stock_qty >= @qty` pattern is the
  ONLY sanctioned way to decrement stock anywhere in this codebase — never replace it with a
  read-check-then-write pattern (`if (stock.Qty >= qty) stock.Qty -= qty;` followed by
  `SaveChanges`), which reintroduces the exact lost-update race this service exists to prevent.
- Every later prompt that needs to change branch stock (P-410, P-510, P-524, P-525, P-608,
  P-705, P-414, P-413) MUST call this service, never touch `branch_inventory.stock_qty` or
  insert `inventory_movements` rows directly.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter InventoryStockServiceTests
dotnet test backend/tests/StoreOS.ConcurrencyTests --filter InventoryStockConcurrencyTests
# Expected: concurrency test proves exactly one winner, final stock never negative.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-406 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-406 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-406` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IInventoryStockService` interface signature (both methods), and the hard
rule that this is the ONLY sanctioned path for mutating branch stock anywhere in the codebase.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-406: <one-line summary>"` only after verification is
   fully green.
