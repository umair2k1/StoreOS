# P-403 — branch_inventory entity + migration + RLS policy

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-403** of the StoreOS Cloud build ("branch_inventory entity + migration + RLS policy") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-401, P-402 complete: `products` table/CRUD exist.
- No per-branch stock table exists yet. This is the FIRST real branch-scoped operational
  table (following the Archetype B pattern proven in P-110's fixture table).

## 3. CONTEXT BLOCK

Target DDL (Plan §8.4):

```sql
CREATE TABLE branch_inventory (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  stock_qty int NOT NULL DEFAULT 0 CHECK (stock_qty >= 0),
  cost_price numeric(18,2) NOT NULL DEFAULT 0,
  sell_price_override numeric(18,2) NULL,
  low_stock_threshold int NOT NULL DEFAULT 3,
  UNIQUE (tenant_id, branch_id, product_id)
);
```

Create `backend/src/StoreOS.Domain/Inventory/BranchInventory.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public class BranchInventory
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public int StockQty { get; private set; }
    public decimal CostPrice { get; private set; }
    public decimal? SellPriceOverride { get; private set; }
    public int LowStockThreshold { get; private set; } = 3;

    private BranchInventory() { }

    public static BranchInventory Create(Guid tenantId, Guid branchId, Guid productId)
        => new() { Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchId, ProductId = productId };

    public void IncreaseStock(int qty, decimal? newCostPrice = null)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");
        StockQty += qty;
        if (newCostPrice is not null && newCostPrice.Value > 0) CostPrice = newCostPrice.Value;
    }

    public void DecreaseStock(int qty)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");
        if (StockQty < qty) throw new DomainValidationException("insufficient_stock", "Not enough stock available.");
        StockQty -= qty;
    }

    public void SetSellPriceOverride(decimal? price)
    {
        if (price is not null && price < 0) throw new DomainValidationException("invalid_sell_price", "Sell price cannot be negative.");
        SellPriceOverride = price;
    }

    public void SetLowStockThreshold(int threshold)
    {
        if (threshold < 0) throw new DomainValidationException("invalid_threshold", "Threshold cannot be negative.");
        LowStockThreshold = threshold;
    }

    public decimal ResolveSellPrice(decimal productSellPrice) => SellPriceOverride ?? productSellPrice;
}
```

Note `IncreaseStock`/`DecreaseStock` here are SIMPLE in-memory mutations for domain-model
completeness and unit testing — the ACTUAL production stock mutation path used by real
handlers goes through `IInventoryStockService` (P-406), which performs a CONCURRENCY-SAFE
conditional SQL UPDATE (`WHERE stock_qty >= @qty`) rather than a naive
load-mutate-save round-trip, to avoid the lost-update race Plan §9.4/§31 describes. These
entity methods remain useful for simple, non-concurrent test scenarios and for the domain
model's own invariant protection (`StockQty` never goes negative via `DecreaseStock`'s guard),
but P-406's service is what production handlers actually call for real concurrent
correctness.

Apply the Archetype B RLS policy (verbatim from P-110) to `branch_inventory`.

## 4. TASK

1. Create `BranchInventory.cs` exactly as shown in `StoreOS.Domain/Inventory/`.
2. Create the EF configuration: snake_case columns, FKs to `branches`/`products`, the CHECK
   constraint `stock_qty >= 0` (raw SQL), and `UNIQUE(tenant_id, branch_id, product_id)`.
3. Add `DbSet<BranchInventory> BranchInventories` to `StoreOsDbContext`, add its branch-scoped
   query filter to `QueryFilters.ApplyAll`.
4. Generate migration `010_BranchInventory` including the RLS Archetype B policy (copy
   verbatim from `docs/ARTIFACTS.md`'s P-110 record, substituting the table name).
5. Apply the migration; re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Inventory/BranchInventoryTests.cs`: (a) `IncreaseStock`
   increments correctly and updates cost price when a positive one is supplied; (b)
   `DecreaseStock` beyond available stock throws `insufficient_stock`; (c)
   `ResolveSellPrice` returns the override when set, else falls back to the product's own
   price; (d) `SetLowStockThreshold`/`SetSellPriceOverride` reject negative values.

## 5. HARD CONSTRAINTS

- `stock_qty >= 0` is enforced at BOTH the domain level
  (`DecreaseStock`'s guard) AND the database level (CHECK constraint) — this defense-in-depth
  is intentional; do not remove either layer.
- RepairService products (Invariant 3) MUST NEVER get a `branch_inventory` row — do not create
  any seed/test data that violates this, and note in `docs/ARTIFACTS.md` that P-905's nightly
  reconciliation job will assert this invariant continuously in production.

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
dotnet ef migrations add 010_BranchInventory --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter BranchInventoryTests
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
   `BLOCKED: P-403 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-403 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-403` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `BranchInventory` signature, migration name `010_BranchInventory`, and
the explicit note that production stock mutations go through `IInventoryStockService`
(P-406), not these entity methods directly, for concurrency safety.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-403: <one-line summary>"` only after verification is
   fully green.
