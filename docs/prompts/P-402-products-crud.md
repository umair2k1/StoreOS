# P-402 — Products CRUD endpoints

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-402** of the StoreOS Cloud build ("Products CRUD endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-401 complete: `Product` entity/table/RLS all exist.
- No REST endpoints for managing the catalog exist yet.

## 3. CONTEXT BLOCK

Create vertical slices under `StoreOS.Application/Catalog/Products/`:
`CreateProduct` (`IRequiresPermission`, `inventory:manage`), `UpdateProduct`
(`IRequiresPermission`, `inventory:manage`; supports updating pricing, margin-scheme flag,
description/service fields, name, active status — split into focused methods on the entity as
already defined in P-401), `GetProducts` (query with filters: `category`, `isActive`,
`isMarginScheme`, `search` text on name, pagination `page`/`pageSize` — no special permission,
any authenticated user may browse the catalog for POS search purposes), `GetProductById`.

`GetProductsQuery` shape:
```csharp
public record GetProductsQuery(string? Category, bool? IsActive, bool? IsMarginScheme,
    string? Search, int Page = 1, int PageSize = 50) : IRequest<PagedResult<ProductDto>>;

public record ProductDto(Guid Id, string Name, string Category, decimal SellPrice,
    decimal PurchasePrice, decimal GstRate, bool IsTaxable, bool IsMarginScheme,
    string? HsnCode, string? Description, int? EstimatedDurationMinutes, int? WarrantyDays, bool IsActive);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
```
`PagedResult<T>` is a REUSABLE shape — define it once in `StoreOS.Application/Abstractions/PagedResult.cs`
and reuse it for every later paginated query in this codebase (inventory list, sales history,
customers, suppliers, repairs, etc.) — do not redefine per-module.

`GetProductsHandler` applies filters conditionally (only `WHERE` on a filter if it's provided),
orders by `Name`, and returns `TotalCount` via a separate `CountAsync` alongside the paged
`Skip/Take` query.

Create `backend/src/StoreOS.Api/Controllers/ProductsController.cs`:
`GET /api/products` (query params map to `GetProductsQuery`), `GET /api/products/{id}`,
`POST /api/products`, `PATCH /api/products/{id}`.

## 4. TASK

1. Create `PagedResult<T>` in `StoreOS.Application/Abstractions/` as the shared
   pagination shape.
2. Create the four vertical slices under `StoreOS.Application/Catalog/Products/` exactly as
   described, with validators enforcing: `Name` non-empty, `Category` must be one of
   `ProductCategory.All` (return `invalid_product_category` via the SAME domain exception the
   entity throws — let the entity's own validation surface naturally rather than
   re-implementing the check in the validator, OR validate early in FluentValidation for a
   friendlier 400 instead of relying on the entity's 422 — prefer FluentValidation catching it
   as a 400 field error since this is basic shape validation, not a deep business rule).
3. Create `ProductsController.cs` with the four routes exactly as described.
4. Write `StoreOS.IntegrationTests/Catalog/ProductsControllerTests.cs`: (a) create products of
   each of the 4 categories -> 201 each, RepairService gets the documented defaults; (b)
   create with `inventory:manage` missing -> 403; (c) list with `category=UsedPhone` filter ->
   only UsedPhone products returned; (d) list with `search=iPhone` -> only matching-name
   products; (e) pagination: seed 60 products, `pageSize=50` -> first page has 50 items,
   `TotalCount=60`; second page has 10; (f) update a product's margin-scheme flag on a
   non-UsedPhone item -> 422 `margin_scheme_invalid_category` (verifying the entity-level
   guard surfaces correctly through the HTTP layer).

## 5. HARD CONSTRAINTS

- `PagedResult<T>` must be defined exactly ONCE and reused by every
  future paginated endpoint in this codebase (inventory, sales, customers, suppliers, repairs,
  etc.) — do not let later prompts redefine an equivalent-but-differently-shaped pagination
  wrapper.
- `GetProductsQuery` (browsing the catalog) requires NO special permission — every
  authenticated user needs this for POS search regardless of role; do not gate it.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ProductsControllerTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-402 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-402 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-402` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `PagedResult<T>` shape (reused everywhere later), the `ProductDto` shape,
and the four `ProductsController` routes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-402: <one-line summary>"` only after verification is
   fully green.
