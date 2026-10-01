# P-404 — GET /inventory + low-stock + valuation endpoints

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-404** of the StoreOS Cloud build ("GET /inventory + low-stock + valuation endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-403 complete: `branch_inventory` exists with RLS.
- `ValuationCalculator` (P-205) is a pure function awaiting real data. No inventory-facing
  REST endpoints exist yet.

## 3. CONTEXT BLOCK

Create `StoreOS.Application/Inventory/Queries/GetInventoryQuery.cs`:

```csharp
public record GetInventoryQuery(string? Category, string? StockStatus, bool? IsMarginScheme,
    string? Search, Guid[]? BranchIds, int Page = 1, int PageSize = 50) : IRequest<PagedResult<InventoryLineDto>>;

public record InventoryLineDto(Guid ProductId, Guid BranchId, string ProductName, string Category,
    int StockQty, decimal CostPrice, decimal EffectiveSellPrice, decimal GstRate,
    bool IsMarginScheme, string? HsnCode, string StockStatus);
```

`StockStatus` values: `"InStock"`, `"LowStock"`, `"OutOfStock"` — computed per-row using
`ValuationCalculator.IsLowStock(qty, threshold)` / `IsOutOfStock(qty)` from P-205, NOT stored
as a column. `GetInventoryHandler` joins `BranchInventory` to `Product`, filters conditionally,
computes `StockStatus` and `EffectiveSellPrice` (via `BranchInventory.ResolveSellPrice`) in
memory after materializing the page (since these are computed, not queryable, properties) —
but apply `Category`/`Search` filters and pagination at the SQL level BEFORE materializing to
avoid pulling the whole table into memory; `StockStatus` filtering, since it's a computed
value, must either be translated to an equivalent SQL predicate (`StockStatus == "OutOfStock"`
-> `StockQty <= 0`; `"LowStock"` -> `StockQty > 0 && StockQty <= LowStockThreshold`; `"InStock"`
-> `StockQty > LowStockThreshold`) applied at the SQL level too, rather than filtering
in-memory after paging (which would break pagination counts) — implement it this way.

Create `StoreOS.Application/Inventory/Queries/GetLowStockQuery.cs`
(`IRequest<IReadOnlyList<InventoryLineDto>>`, no pagination, returns ALL low+out-of-stock
physical items across the caller's accessible branches, explicitly EXCLUDING `RepairService`
category rows since (per Invariant 3 / P-403's note) none should exist anyway, but filter
defensively regardless).

Create `StoreOS.Application/Inventory/Queries/GetValuationQuery.cs`:

```csharp
public record GetValuationQuery(Guid[]? BranchIds) : IRequest<ValuationDto>;
public record ValuationDto(decimal CostValuation, decimal RetailValuation, decimal PotentialGrossValue);
```

`GetValuationHandler` loads all `BranchInventory` joined to `Product` (category, qty, cost,
resolved sell price) for the requested/accessible branches, maps to
`StockValuationLine` records, and calls `ValuationCalculator.Calculate(...)` (P-205) — this is
the FIRST real caller of that pure domain function, proving the wiring end-to-end.

Route these through `ReportingDbContext` (P-119) since they are read-heavy aggregate queries
per Plan §11's routing table — inject `ReportingDbContext` instead of `StoreOsDbContext` in
all three handlers.

Create `backend/src/StoreOS.Api/Controllers/InventoryController.cs`:
`GET /api/inventory`, `GET /api/inventory/low-stock`, `GET /api/inventory/valuation`.

## 4. TASK

1. Create the three query slices under `StoreOS.Application/Inventory/Queries/` exactly
   as described, each handler injecting `ReportingDbContext`.
2. Create `InventoryController.cs` with the three routes.
3. Write `StoreOS.IntegrationTests/Inventory/InventoryQueriesTests.cs`: seed branch inventory
   rows spanning `InStock`/`LowStock`(qty<=3)/`OutOfStock`(qty<=0) states across 2 branches,
   plus a `RepairService` product (confirm it correctly has NO branch_inventory row and is
   never accidentally included). Assert: (a) `GET /api/inventory?stockStatus=LowStock` returns
   only the qty-1-to-3 rows; (b) `GET /api/inventory/low-stock` matches; (c)
   `GET /api/inventory/valuation` returns cost/retail/potential values that hand-computed match
   `Σ(qty*cost)`/`Σ(qty*sell)` EXCLUDING any service rows, using the exact `ValuationCalculator`
   math from P-205; (d) a user scoped to only branch A does not see branch B's inventory rows
   or have them counted in valuation.

## 5. HARD CONSTRAINTS

- `StockStatus` filtering must be translated into a SQL-level
  predicate BEFORE pagination — never filter computed values in-memory after `Skip/Take`,
  which would silently corrupt `TotalCount` and page boundaries.
- All three endpoints route through `ReportingDbContext`, not `StoreOsDbContext` — confirm this
  explicitly, since it is the FIRST real usage of the reporting seam from P-119.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter InventoryQueriesTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-404 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-404 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-404` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the three query/DTO shapes and the `InventoryController` routes, and
confirmation these are the first production callers of `ReportingDbContext` and
`ValuationCalculator`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-404: <one-line summary>"` only after verification is
   fully green.
