# P-503 — GET /pos/search + GET /pos/product/{id} endpoints

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-503** of the StoreOS Cloud build ("GET /pos/search + GET /pos/product/{id} endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-502 complete: `Sale`/`SaleItem` schema exists (unused by any handler yet).
- P-403/P-404 (branch_inventory + queries), P-407/P-408 (IMEIs) all complete.
- No POS-facing search endpoint exists yet — this is the first screen of the actual billing
  workflow (Requirements §6.1/§6.2).

## 3. CONTEXT BLOCK

Create `StoreOS.Application/Sales/Search/PosSearchQuery.cs`:

```csharp
public record PosSearchQuery(string Q) : IRequest<IReadOnlyList<PosSearchResultDto>>;

public record PosSearchResultDto(Guid ProductId, string Name, string Category, decimal SellPrice,
    decimal GstRate, string? HsnCode, bool IsMarginScheme, int StockQty, string StockStatus);
```

`PosSearchHandler` (branch-scoped to `IBranchContext.CurrentBranchId` when present, else the
caller's full accessible set for a "browse all my branches" scenario — but POS billing always
happens IN a branch context, so default to requiring one; if absent, fall back to matching
against ALL accessible branches' stock as a reasonable read-only default since `GET` requests
don't strictly require `X-Branch-Id` per P-309): matches `Q` against `products.name`
case-insensitively (`ILIKE '%' || @q || '%'`) AND, separately, exact IMEI match via a join to
`inventory_imeis` (Requirements §6.2: barcode/IMEI scanner input) — if `Q` looks like a
plausible IMEI (14-16 digits), ALSO search `inventory_imeis.imei` for an exact match and
surface that product with its specific IMEI's availability, in ADDITION to the normal name
search results (union the two result sets, de-duplicating by product id, preferring the more
specific IMEI-matched entry when both fire for the same product). For `RepairService` category
products, `StockQty`/`StockStatus` are meaningless — return `StockQty = 0` and
`StockStatus = "N/A"` for services (never "OutOfStock", to avoid confusing UI badges on
services, which are always sellable regardless of any notion of stock, per Requirements §6.4
"For repair services: Stock validation does not apply").

`GetPosProductQuery` (`GET /pos/product/{id}`) returns a richer single-product DTO including
the list of currently-`InStock` IMEIs at the current branch (for phones), so the POS UI can
present an IMEI picker:

```csharp
public record PosProductDetailDto(Guid ProductId, string Name, string Category, decimal SellPrice,
    decimal GstRate, string? HsnCode, bool IsMarginScheme, int StockQty, string StockStatus,
    IReadOnlyList<string> AvailableImeis);
```

Create `backend/src/StoreOS.Api/Controllers/PosController.cs` with
`GET /api/pos/search?q=`, `GET /api/pos/product/{id}`.

## 4. TASK

1. Create `PosSearchQuery.cs`/handler and `GetPosProductQuery.cs`/handler under
   `StoreOS.Application/Sales/Search/`, exactly matching the DTOs described.
2. Create `PosController.cs` with the two GET routes.
3. Write `StoreOS.IntegrationTests/Sales/PosSearchTests.cs`: (a) name search returns matching
   products with correct branch-scoped `StockQty`/`StockStatus`; (b) an exact IMEI-shaped query
   returns the product that owns that IMEI, even if the IMEI string bears no resemblance to the
   product's name; (c) a `RepairService` product appears in name search results with
   `StockStatus = "N/A"`, never `"OutOfStock"`; (d) `GET /pos/product/{id}` for an IMEI-tracked
   phone lists exactly its currently-`InStock` IMEIs at the caller's branch, excluding `Sold`/
   `InTransit`/`ReturnedToSupplier` ones; (e) a user scoped to branch A does not see branch B's
   stock quantities for the same shared tenant-wide product (confirms the branch-scoping is
   correctly applied to THIS specific query, not just relying on generic RLS).

## 5. HARD CONSTRAINTS

- `RepairService` products must NEVER report `StockStatus =
  "OutOfStock"` or `"LowStock"` — always `"N/A"`, matching Requirements §6.4's explicit
  stock-validation exemption for services.
- IMEI search must be an EXACT match only (no partial/fuzzy IMEI matching) — a barcode scanner
  produces the complete, exact IMEI string.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter PosSearchTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-503 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-503 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-503` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `PosSearchResultDto`/`PosProductDetailDto` shapes and the two
`PosController` GET routes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-503: <one-line summary>"` only after verification is
   fully green.
