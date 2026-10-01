# P-205 — InventoryValuation pure calculator

**Phase:** 2 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-205** of the StoreOS Cloud build ("InventoryValuation pure calculator") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-201–204 complete: tax, discount, finance engines all tested.
- No inventory valuation calculator exists yet. This is a pure function operating on
  in-memory data (no DB access) — it will be called by P-404's `GET /inventory/valuation`
  endpoint, which supplies the actual stock rows from the database.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Domain/Inventory/StockValuationLine.cs`:

```csharp
namespace StoreOS.Domain.Inventory;

public readonly record struct StockValuationLine(string Category, int Qty, decimal PurchasePrice, decimal SellPrice);
```

Create `backend/src/StoreOS.Domain/Inventory/ValuationResult.cs`:

```csharp
namespace StoreOS.Domain.Inventory;

public readonly record struct ValuationResult(decimal CostValuation, decimal RetailValuation, decimal PotentialGrossValue);
```

Create `backend/src/StoreOS.Domain/Inventory/ValuationCalculator.cs` (Plan/Requirements §11.5):

```csharp
using StoreOS.Domain.Common;

namespace StoreOS.Domain.Inventory;

public static class ValuationCalculator
{
    private const string RepairServiceCategory = "RepairService";

    public static ValuationResult Calculate(IEnumerable<StockValuationLine> lines)
    {
        decimal cost = 0m, retail = 0m;
        foreach (var line in lines)
        {
            if (line.Category == RepairServiceCategory) continue; // Invariant 3: services never count as stock
            cost += line.Qty * line.PurchasePrice;
            retail += line.Qty * line.SellPrice;
        }
        cost = Money.Round2(cost);
        retail = Money.Round2(retail);
        return new ValuationResult(cost, retail, Money.Round2(retail - cost));
    }

    public static bool IsLowStock(int qty, int threshold) => qty <= threshold;
    public static bool IsOutOfStock(int qty) => qty <= 0;
}
```

Exact required test cases:

1. Three lines: `("NewPhone", 5, 10000m, 12000m)`, `("Accessory", 20, 100m, 150m)`,
   `("RepairService", 999, 500m, 800m)` (a service line that MUST be excluded even though it
   has absurd qty/price to make the exclusion obvious if the filter is broken). Expected:
   cost = 5*10000 + 20*100 = 52000.00; retail = 5*12000 + 20*150 = 63000.00;
   potential = 63000-52000 = 11000.00. The RepairService line must contribute NOTHING.
2. Empty list -> all zeros.
3. Single line with `qty=0` -> contributes 0 to both cost and retail (still included in the
   loop, just multiplies to zero — confirm this doesn't crash and returns 0,0,0 for a
   single-zero-qty-line list).
4. `IsLowStock(3, 3)` -> `true` (`qty <= threshold`, boundary inclusive per Requirements §5.3
   "at or below").
5. `IsLowStock(4, 3)` -> `false`.
6. `IsOutOfStock(0)` -> `true`; `IsOutOfStock(-1)` -> `true` (defensive, though negative stock
   should never occur per DB CHECK constraints added in P-403); `IsOutOfStock(1)` -> `false`.

## 4. TASK

1. Create `StockValuationLine.cs`, `ValuationResult.cs`, `ValuationCalculator.cs`
   exactly as shown in `StoreOS.Domain/Inventory/`.
2. Write `backend/tests/StoreOS.Domain.Tests/Inventory/ValuationCalculatorTests.cs`
   implementing all 6 numbered cases with exact expected decimal values.

## 5. HARD CONSTRAINTS

- The `"RepairService"` category string literal here MUST exactly
  match the category value used later in `products.category` CHECK constraint (P-401:
  `'NewPhone','UsedPhone','Accessory','RepairService'`) — keep these string values
  synchronized across the whole codebase; do not introduce a different casing or spelling
  anywhere.
- `IsLowStock`'s `<=` boundary and `IsOutOfStock`'s `<=` boundary must exactly match
  Requirements §5.3/§11.4's stated thresholds (`qty <= 3` default low-stock,
  `qty <= 0` out-of-stock) — do not use `<` anywhere in these two functions.

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter ValuationCalculatorTests
# Expected: 6 passed, including the RepairService-exclusion case with exact numeric values.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-205 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-205 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-205` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `StockValuationLine`, `ValuationResult`, and `ValuationCalculator`
signatures, and the fixed category string literal `"RepairService"` used for the exclusion
check (must match `products.category` values exactly).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-205: <one-line summary>"` only after verification is
   fully green.
