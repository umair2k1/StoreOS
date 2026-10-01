# P-410 — POST /purchases stock-in endpoint (with optional IMEIs)

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-410** of the StoreOS Cloud build ("POST /purchases stock-in endpoint (with optional IMEIs)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-409 complete: `Purchase` entity/table exist.
- `IInventoryStockService.IncreaseStockAsync` (P-406) and `InventoryImei.Create` (P-407) both
  exist but have not yet been wired together into a real purchase/stock-in workflow.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Purchasing/StockIn/StockInCommand.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Purchasing.StockIn;

public record StockInCommand(Guid ProductId, int Qty, decimal PurchaseRate,
    Guid? SupplierId, string? SupplierName, string? SupplierPhone, string? SupplierAddress,
    DateOnly PurchaseDate, string? SupplierInvoiceNo, IReadOnlyList<string>? Imeis)
    : IRequest<StockInResult>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}

public record StockInResult(Guid PurchaseId, int NewStockQty, IReadOnlyList<Guid> CreatedImeiIds);
```

`StockInValidator.cs`: `Qty > 0`; `PurchaseRate >= 0`; if `Imeis` is provided, its `Count` must
EQUAL `Qty` exactly (Requirements §12.3: "Create one unit-level IMEI record per physical
phone"), and every IMEI string must be non-empty and unique WITHIN the submitted list (reject
duplicates in the same request payload before even hitting the DB's own uniqueness check).

`StockInHandler.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;

namespace StoreOS.Application.Purchasing.StockIn;

public class StockInHandler : IRequestHandler<StockInCommand, StockInResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;
    private readonly IAuditWriter _audit;

    public StockInHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext,
        IInventoryStockService stockService, IAuditWriter audit)
    {
        _db = db; _scope = scope; _branchContext = branchContext; _stockService = stockService; _audit = audit;
    }

    public async Task<StockInResult> Handle(StockInCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value; // guaranteed non-null by BranchContextMiddleware for this POST

        var purchase = Purchase.Create(_scope.TenantId, branchId, request.ProductId, request.Qty,
            request.PurchaseRate, request.SupplierId, request.SupplierName, request.SupplierPhone,
            request.SupplierAddress, request.PurchaseDate, _scope.UserId, request.SupplierInvoiceNo);
        await _db.Set<StoreOS.Domain.Purchasing.Purchase>().AddAsync(purchase, ct);
        await _db.SaveChangesAsync(ct); // flush to get purchase.Id for IMEI linkage below

        await _stockService.IncreaseStockAsync(branchId, request.ProductId, request.Qty,
            request.PurchaseRate, MovementType.Purchase, "Purchase", purchase.Id.ToString(), ct);

        var createdImeiIds = new List<Guid>();
        if (request.Imeis is { Count: > 0 })
        {
            foreach (var imeiStr in request.Imeis)
            {
                var imeiRow = InventoryImei.Create(_scope.TenantId, branchId, request.ProductId,
                    purchase.Id, imeiStr, request.PurchaseDate, request.PurchaseRate);
                await _db.Set<InventoryImei>().AddAsync(imeiRow, ct);
                createdImeiIds.Add(imeiRow.Id);
            }
        }

        await _audit.WriteAsync(branchId, "purchase.stock_in", "Purchase", purchase.Id.ToString(),
            new { request.ProductId, request.Qty, request.PurchaseRate, ImeiCount = request.Imeis?.Count ?? 0 }, ct);

        await _db.SaveChangesAsync(ct);

        var newQty = await _db.Set<BranchInventory>()
            .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == request.ProductId)
            .Select(bi => bi.StockQty).FirstAsync(ct);

        return new StockInResult(purchase.Id, newQty, createdImeiIds);
    }
}
```

Create `backend/src/StoreOS.Api/Controllers/PurchasesController.cs`:
`POST /api/purchases` (maps to `StockInCommand`), `GET /api/purchases` (basic list query,
filterable by product/branch/date range — build a simple `GetPurchasesQuery` alongside this
handler using the `PagedResult<T>` convention from P-402).

## 4. TASK

1. Create `StockInCommand.cs`, `StockInValidator.cs`, `StockInHandler.cs`,
   `StockInResult` exactly as shown in `StoreOS.Application/Purchasing/StockIn/`.
2. Create `GetPurchases/GetPurchasesQuery.cs` (+ handler + `PurchaseDto`) as a simple
   paginated list following the P-402 pattern.
3. Create `PurchasesController.cs` with `POST /api/purchases` and `GET /api/purchases`.
4. Write `StoreOS.IntegrationTests/Purchasing/StockInTests.cs`: (a) stock-in WITHOUT IMEIs
   (e.g. accessories) increases `branch_inventory.stock_qty` correctly and creates a
   `Purchase` movement in `inventory_movements`; (b) stock-in WITH IMEIs where
   `Imeis.Count != Qty` -> 400 validation error; (c) stock-in with matching IMEI count creates
   exactly that many `InventoryImei` rows, all `InStock`, each linked to the new `Purchase`
   via `purchase_id`; (d) submitting a duplicate IMEI string within the SAME request -> 400
   (caught by the validator before hitting the DB); (e) submitting an IMEI that is ALREADY
   active elsewhere (seed one first) -> the `SaveChangesAsync` inside the handler throws (wrap
   and surface as a clean 409/422 rather than a raw 500 — add a
   `catch` in the handler or a dedicated exception-translation step for `DbUpdateException`
   wrapping a unique-violation on `ux_imei_active`, converting it to
   `DomainException("imei_already_active", ...)` mapped to 409).

## 5. HARD CONSTRAINTS

- `Imeis.Count` must exactly equal `Qty` — this is validated
  BEFORE any DB writes, in `StockInValidator`, not discovered mid-transaction.
- The handler calls `IInventoryStockService.IncreaseStockAsync`, never manipulates
  `branch_inventory` directly — reuse the sanctioned service from P-406.
- Wrap the final `SaveChangesAsync()` in a translation layer that converts a Postgres
  unique-violation on `ux_imei_active` into a clean `DomainException("imei_already_active",
  ...)` -> 409, rather than letting a raw `DbUpdateException`/`PostgresException` reach the
  generic 500 handler.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter StockInTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-410 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-410 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-410` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `StockInCommand`/`StockInResult` shapes, the `POST /api/purchases`
route, and the `imei_already_active`->409 translation added for unique-constraint violations.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-410: <one-line summary>"` only after verification is
   fully green.
