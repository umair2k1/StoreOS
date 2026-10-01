# P-505 — Server-side pricing resolution (branch override ?? product price) + margin scheme cost cascade wiring

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-505** of the StoreOS Cloud build ("Server-side pricing resolution (branch override ?? product price) + margin scheme cost cascade wiring") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-504 complete: `CheckoutCommand`'s final shape is defined and validated
  structurally.
- `TaxEngine.StandardGst`/`MarginSchemeGst` (P-201/P-202), `DiscountEngine` (P-203),
  `BranchInventory.ResolveSellPrice` (P-403), `IInventoryStockService.LoadImeiForSaleAsync`
  (P-408) all exist. This prompt wires them together into the SERVER-SIDE pricing resolution
  that Plan §6.3 mandates ("Never trust the frontend") and Plan §10.1 step 2 describes.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Sales/Checkout/ResolvedCartLine.cs`:

```csharp
namespace StoreOS.Application.Sales.Checkout;

public record ResolvedCartLine(Guid ProductId, string ProductName, string Category, int Qty,
    decimal PricePerUnit, decimal? PurchasePriceSnapshot, string? HsnCode, decimal GstRate,
    bool IsMarginScheme, bool IsGift, string? Imei, Guid? ImeiRowId, decimal LineTaxable,
    decimal LineCgst, decimal LineSgst);
```

Create `backend/src/StoreOS.Application/Sales/Checkout/PricingResolver.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tax;

namespace StoreOS.Application.Sales.Checkout;

public class PricingResolver
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IInventoryStockService _stockService;

    public PricingResolver(IAppDbContext db, IAccessScope scope, IInventoryStockService stockService)
    { _db = db; _scope = scope; _stockService = stockService; }

    public async Task<ResolvedCartLine> ResolveAsync(Guid branchId, CheckoutCartLine line, CancellationToken ct)
    {
        var product = await _db.Set<StoreOS.Domain.Catalog.Product>().FirstOrDefaultAsync(p => p.Id == line.ProductId, ct)
            ?? throw new DomainValidationException("product_not_found", $"Product {line.ProductId} not found.");
        if (!product.IsActive)
            throw new DomainValidationException("product_inactive", $"Product '{product.Name}' is not active.");

        var branchInv = await _db.Set<BranchInventory>()
            .FirstOrDefaultAsync(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId
                && bi.ProductId == product.Id, ct);

        decimal basePrice = branchInv?.ResolveSellPrice(product.SellPrice) ?? product.SellPrice;
        decimal pricePerUnit = line.OverridePricePerUnit ?? basePrice;
        // NOTE: applying an OverridePricePerUnit requires 'sales:discount:apply' permission —
        // that authorization check happens in the calling CheckoutHandler (P-510), not here;
        // this resolver is pure pricing math once authorization has already been established.

        Guid? imeiRowId = null;
        decimal? costBasis = null;

        if (!string.IsNullOrEmpty(line.Imei))
        {
            var imeiSnapshot = await _stockService.LoadImeiForSaleAsync(branchId, line.Imei, ct);
            if (imeiSnapshot.ProductId != product.Id)
                throw new DomainValidationException("imei_product_mismatch",
                    $"IMEI {line.Imei} does not belong to product '{product.Name}'.");
            imeiRowId = imeiSnapshot.Id;
            costBasis = imeiSnapshot.PurchasePrice;
        }
        else if (line.UseMarginScheme)
        {
            costBasis = (branchInv?.CostPrice ?? 0m) > 0 ? branchInv!.CostPrice : product.PurchasePrice;
        }

        LineTax tax;
        if (line.UseMarginScheme)
        {
            if (product.Category != ProductCategory.UsedPhone)
                throw new DomainValidationException("margin_scheme_invalid_category",
                    "Margin scheme can only be applied to Used Phone items.");
            if (costBasis is null || costBasis <= 0)
                throw new DomainValidationException("margin_scheme_invalid_cost_basis",
                    $"Margin scheme requires a known positive cost basis for '{product.Name}'. " +
                    "Please update inventory cost before billing under margin scheme.");
            tax = TaxEngine.MarginSchemeGst(pricePerUnit * line.Qty, costBasis.Value * line.Qty, product.GstRate);
        }
        else
        {
            tax = line.IsGift ? LineTax.Zero : TaxEngine.StandardGst(pricePerUnit * line.Qty, product.GstRate);
        }

        return new ResolvedCartLine(product.Id, product.Name, product.Category, line.Qty,
            pricePerUnit, costBasis, product.HsnCode, product.GstRate, line.UseMarginScheme,
            line.IsGift, line.Imei, imeiRowId, tax.Taxable, tax.Cgst, tax.Sgst);
    }
}
```

Note: a GIFT line still computes `pricePerUnit`/stock/IMEI resolution normally (so inventory
correctly decrements and the invoice can display what was given away), but its tax is
`LineTax.Zero` since Requirements §6.8 says gift lines "must not be treated as normal sales
revenue" — zero taxable/CGST/SGST is the simplest correct representation; the line still has a
nonzero `PricePerUnit` recorded for informational/audit purposes on the invoice.

The IMEI cost-cascade (Requirements §7.2 exactly): IMEI present -> `inventory_imeis
.purchase_price`; no IMEI, margin scheme requested -> `branch_inventory.cost_price` if `> 0`,
else `products.purchase_price`; either way, `costBasis <= 0` at the point of USE (not just at
IMEI creation time, which P-407 already guards) throws `margin_scheme_invalid_cost_basis`,
enforcing Requirements §7.2's hard validation guard at the actual pricing moment too
(defense in depth alongside P-407's stock-in-time guard and P-202's pure-function guard).

## 4. TASK

1. Create `ResolvedCartLine.cs` and `PricingResolver.cs` exactly as shown in
   `StoreOS.Application/Sales/Checkout/`, register `PricingResolver` scoped.
2. Write `StoreOS.IntegrationTests/Sales/PricingResolverTests.cs` covering, with real seeded
   products/branch_inventory/IMEIs: (a) a standard `NewPhone` line with no margin scheme, no
   override -> uses `branch_inventory.sell_price_override` when set, else `products
   .sell_price`, and computes `StandardGst` correctly; (b) a `UsedPhone` line WITH an IMEI and
   margin scheme enabled -> cost basis comes from `inventory_imeis.purchase_price` specifically
   (seed a DIFFERENT branch_inventory.cost_price to prove the IMEI-specific cost wins over the
   branch default when both exist); (c) a `UsedPhone` line WITHOUT an IMEI, margin scheme
   enabled, `branch_inventory.cost_price > 0` -> uses that; (d) same but
   `branch_inventory.cost_price == 0` -> falls back to `products.purchase_price`; (e) margin
   scheme requested on an `Accessory` -> `margin_scheme_invalid_category`; (f) margin scheme
   with a fully unseeded cost (both branch cost and product purchase price are 0) ->
   `margin_scheme_invalid_cost_basis` (422); (g) a gift line -> `LineTax` is all zeros
   regardless of category/price; (h) an IMEI belonging to a DIFFERENT product than the one
   requested -> `imei_product_mismatch`; (i) an inactive product -> `product_inactive`.

## 5. HARD CONSTRAINTS

- The cost cascade order is FIXED: IMEI's own `purchase_price` first
  (when an IMEI is on the line), THEN `branch_inventory.cost_price` (if `> 0`), THEN
  `products.purchase_price` as the final fallback — never reorder these, never skip the IMEI
  case even when margin scheme wasn't explicitly requested for that line (an IMEI's own cost is
  ALWAYS the right cost source when present, margin-scheme-or-not, since it's later used for
  `purchase_price_snapshot` on the sale_item regardless).
- `margin_scheme_invalid_cost_basis` and `margin_scheme_invalid_category` are DomainValidationException
  codes that map to 422 via the existing `ExceptionHandlingMiddleware` — no new mapping needed
  since the default DomainException branch already produces 422.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter PricingResolverTests
# Expected: all 9 cases pass, confirming the exact cost cascade and tax computation.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-505 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-505 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-505` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ResolvedCartLine`/`PricingResolver.ResolveAsync` signatures and the
precise, fixed cost-cascade order (IMEI -> branch cost -> product cost) for reuse by anyone
reading this later.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-505: <one-line summary>"` only after verification is
   fully green.
