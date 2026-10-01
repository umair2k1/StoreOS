# P-524 — POST /sales/{id}/void (live-permission gated, reversing movements+ledger)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-524** of the StoreOS Cloud build ("POST /sales/{id}/void (live-permission gated, reversing movements+ledger)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-523 complete: subsequent payments work correctly as immutable rows.
- No void workflow exists yet. Per Plan Section 10.2/Requirements Section 15.2 and Invariant 6,
  voiding must PRESERVE the original sale record while reversing its inventory/ledger effects
  via NEW, separate entries — never deleting or silently rewriting history.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Sales/VoidSale/VoidSaleCommand.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.VoidSale;

public record VoidSaleCommand(Guid SaleId, string Reason) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "sales:void";
}
```

`VoidSaleValidator.cs`: `Reason` `NotEmpty()`.

`VoidSaleHandler.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.VoidSale;

public class VoidSaleHandler : IRequestHandler<VoidSaleCommand>
{
    private readonly IAppDbContext _db;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAuditWriter _audit;

    public VoidSaleHandler(IAppDbContext db, IInventoryStockService stockService,
        ICustomerLedgerService ledgerService, IAuditWriter audit)
    { _db = db; _stockService = stockService; _ledgerService = ledgerService; _audit = audit; }

    public async Task Handle(VoidSaleCommand request, CancellationToken ct)
    {
        var sale = await _db.Set<Sale>().Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new DomainValidationException("sale_not_found", "Sale not found.");

        sale.MarkVoided(); // throws if not currently Active (guards double-void)

        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var item in sale.Items.Where(i => ProductCategory.IsPhysical(i.Category) && !i.IsGift))
        {
            if (!string.IsNullOrEmpty(item.Imei))
            {
                var imeiRow = await _db.Set<InventoryImei>()
                    .FirstAsync(i => i.TenantId == sale.TenantId && i.SaleId == sale.Id && i.SaleItemId == item.Id, ct);
                imeiRow.RestoreToStock();
                await _stockService.IncreaseStockAsync(sale.BranchId, item.ProductId!.Value, item.Qty,
                    null, MovementType.SaleVoid, "SaleVoid", sale.Id.ToString(), ct);
            }
            else
            {
                await _stockService.IncreaseStockAsync(sale.BranchId, item.ProductId!.Value, item.Qty,
                    null, MovementType.SaleVoid, "SaleVoid", sale.Id.ToString(), ct);
            }
        }

        if (sale.CustomerId is not null && sale.InvoiceType == InvoiceType.TaxInvoice
            && sale.PaymentMode != StoreOS.Domain.Sales.PaymentMode.Finance)
        {
            await _ledgerService.RecordVoidReversalAsync(sale.CustomerId.Value, sale.Id,
                sale.GrandTotal, sale.BranchId, businessDate, ct);
            var customer = await _db.Set<StoreOS.Domain.Customers.Customer>()
                .FirstAsync(c => c.Id == sale.CustomerId.Value, ct);
            customer.SubtractFromTotalPurchases(sale.GrandTotal);
        }

        await _audit.WriteAsync(sale.BranchId, "sale.voided", "Sale", sale.Id.ToString(),
            new { request.Reason, sale.InvoiceNumber, sale.GrandTotal }, ct);

        await _db.SaveChangesAsync(ct);
    }
}
```

Add `POST /api/sales/{id}/void` to `SalesController.cs`. `VoidSaleCommand` is
`IHighImpactOperation` (live-DB permission check, per Plan Section 8.3's explicit listing of
`sales:void` as a high-impact operation) — NOT `IRequiresPermission`.

## 4. TASK

1. Create `VoidSaleCommand.cs`, `VoidSaleValidator.cs`, `VoidSaleHandler.cs` exactly as
   shown (reconciling navigation/property names against your actual `Sale`/`SaleItem`/
   `InventoryImei` signatures from `docs/ARTIFACTS.md`), in
   `StoreOS.Application/Sales/VoidSale/`.
2. Add `POST /api/sales/{id}/void` to `SalesController.cs`.
3. Write `StoreOS.IntegrationTests/Sales/VoidSaleTests.cs`: (a) voiding an active sale sets
   `status='Voided'` while the ORIGINAL row (invoice number, line items, amounts) remains
   completely intact and queryable — assert every original field is byte-identical to before
   the void, only `status` changed; (b) stock is correctly restored for non-IMEI physical
   lines; (c) an IMEI-tracked line's IMEI transitions `Sold -> InStock`; (d) a gift line's
   quantity is NOT restored to stock... wait, reconsider: Requirements do not explicitly say
   gift-line stock should be exempted from void-reversal — a gift STILL consumed physical stock
   at checkout (per P-514's design), so voiding the WHOLE sale should still restore ALL
   physical lines' stock including gifted ones. Correct the handler: REMOVE the `&& !i.IsGift`
   filter from the reversal loop (gift lines DO get their stock restored on void, matching that
   they DID decrement stock at sale time) — apply this correction and write the test to prove
   gift-line stock IS correctly restored on void; (e) voiding an ALREADY-voided sale ->
   `DomainValidationException` (from `Sale.MarkVoided`'s own guard) surfaces as a clean 422; (f)
   customer ledger gets an `InvoiceVoid` credit reversing the original debit, and
   `customers.total_purchases` decreases correctly; (g) a caller WITHOUT `sales:void` -> 403 via
   live-DB check (immediate effect on permission revocation, reusing the P-308 test pattern).

## 5. HARD CONSTRAINTS

- CORRECT the handler to remove the erroneous `&& !i.IsGift` filter
  from the stock-reversal loop before finalizing — gift lines consumed real stock at sale time
  (P-514) and must have that stock correctly restored on void, just like any other physical
  line.
- The ORIGINAL `Sale` row is NEVER deleted and NEVER has its financial fields (grand_total,
  taxable, cgst, sgst, etc.) rewritten — only `status` changes, via `MarkVoided()`. This is
  Invariant 6's core enforcement point.
- `VoidSaleCommand` implements `IHighImpactOperation`, not `IRequiresPermission` — it must get
  the zero-cache-lag live-DB permission check.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter VoidSaleTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-524 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-524 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-524` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `VoidSaleCommand`/handler signatures, the `POST /api/sales/{id}/void`
route, and the correction removing the gift-line exemption from stock reversal.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-524: <one-line summary>"` only after verification is
   fully green.
