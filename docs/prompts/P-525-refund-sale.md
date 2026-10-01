# P-525 — POST /sales/{id}/refund

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-525** of the StoreOS Cloud build ("POST /sales/{id}/refund") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-524 complete: void workflow fully works with correct stock/ledger reversal.
- No refund workflow exists yet. Per Requirements Section 15.3/Invariant 6, a refund is
  DIFFERENT from a void: a void represents cancelling the ENTIRE transaction (e.g. a mistaken
  sale never delivered), while a refund represents the customer RETURNING goods/getting money
  back AFTER a legitimate sale (e.g. a defective phone returned under warranty) — the original
  sale is equally preserved in both cases, but the semantic and typical partial-vs-whole scope
  differs.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Sales/RefundSale/RefundSaleCommand.cs`.
Support LINE-LEVEL refunds (a customer might return only ONE item from a multi-item sale) per
Requirements Section 15.3 "Mark the appropriate sale/line as refunded according to business
rules":

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.RefundSale;

public record RefundSaleCommand(Guid SaleId, IReadOnlyList<Guid>? SaleItemIds, string Reason,
    decimal RefundAmount) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "sales:refund";
}
```

`SaleItemIds == null` means a WHOLE-SALE refund (all lines); a non-null list means only those
specific lines are being refunded (their stock/IMEIs are restored, but OTHER lines on the same
sale remain sold). `RefundAmount` is the actual cash/credit amount being returned to the
customer, which the caller specifies explicitly (it may be less than the full line value if a
restocking fee applies — this system does not compute it automatically, matching Requirements
Section 15.3's "according to business rules" phrasing, which implies staff judgment).

`RefundSaleHandler.cs`:

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

namespace StoreOS.Application.Sales.RefundSale;

public class RefundSaleHandler : IRequestHandler<RefundSaleCommand>
{
    private readonly IAppDbContext _db;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAuditWriter _audit;

    public RefundSaleHandler(IAppDbContext db, IInventoryStockService stockService,
        ICustomerLedgerService ledgerService, IAuditWriter audit)
    { _db = db; _stockService = stockService; _ledgerService = ledgerService; _audit = audit; }

    public async Task Handle(RefundSaleCommand request, CancellationToken ct)
    {
        var sale = await _db.Set<Sale>().Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new DomainValidationException("sale_not_found", "Sale not found.");
        if (sale.Status != SaleStatus.Active)
            throw new DomainValidationException("sale_not_active", "Only an Active sale can be refunded.");
        if (request.RefundAmount <= 0 || request.RefundAmount > sale.GrandTotal)
            throw new DomainValidationException("invalid_refund_amount", "Refund amount must be positive and not exceed the sale's grand total.");

        var linesToRefund = request.SaleItemIds is null
            ? sale.Items.ToList()
            : sale.Items.Where(i => request.SaleItemIds.Contains(i.Id)).ToList();
        if (linesToRefund.Count == 0)
            throw new DomainValidationException("no_lines_to_refund", "No matching sale lines found to refund.");

        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var item in linesToRefund.Where(i => ProductCategory.IsPhysical(i.Category)))
        {
            if (!string.IsNullOrEmpty(item.Imei))
            {
                var imeiRow = await _db.Set<InventoryImei>()
                    .FirstAsync(i => i.TenantId == sale.TenantId && i.SaleId == sale.Id && i.SaleItemId == item.Id, ct);
                imeiRow.RestoreToStock();
            }
            await _stockService.IncreaseStockAsync(sale.BranchId, item.ProductId!.Value, item.Qty,
                null, MovementType.SaleRefund, "SaleRefund", sale.Id.ToString(), ct);
        }

        var isWholeSaleRefund = request.SaleItemIds is null;
        sale.MarkRefunded(); // if partial, still marks the sale status per current simplified model — see note below

        if (sale.CustomerId is not null && sale.InvoiceType == InvoiceType.TaxInvoice
            && sale.PaymentMode != StoreOS.Domain.Sales.PaymentMode.Finance)
        {
            await _ledgerService.RecordRefundCreditAsync(sale.CustomerId.Value, sale.Id,
                request.RefundAmount, sale.BranchId, businessDate, ct);
            var customer = await _db.Set<StoreOS.Domain.Customers.Customer>()
                .FirstAsync(c => c.Id == sale.CustomerId.Value, ct);
            customer.SubtractFromTotalPurchases(request.RefundAmount);
        }

        await _audit.WriteAsync(sale.BranchId, "sale.refunded", "Sale", sale.Id.ToString(),
            new { request.Reason, request.RefundAmount, IsWholeSaleRefund = isWholeSaleRefund,
                  RefundedLineIds = linesToRefund.Select(l => l.Id) }, ct);

        await _db.SaveChangesAsync(ct);
    }
}
```

**Important design note to resolve during implementation**: `Sale.MarkRefunded()` (from
P-502) transitions the WHOLE sale's `Status` to `Refunded` — but Requirements Section 15.1
lists `Refunded` as a whole-sale status alongside `Active`/`Voided`, with no separate
"partially refunded" state in the DDL (`sales.status CHECK (status IN ('Active','Voided',
'Refunded'))` per Plan Section 8.5 — no fourth value). For a PARTIAL line-level refund, marking
the entire sale `Refunded` would incorrectly suggest the WHOLE sale is gone, when other lines
remain legitimately sold. Resolve this deliberately and document it clearly: only transition
`sale.Status` to `Refunded` when `isWholeSaleRefund` is true (all lines refunded, or
`SaleItemIds` was null); for a PARTIAL refund, do NOT change `sale.Status` at all (it remains
`Active`, since the sale as a whole is still valid, just missing certain returned lines) — the
partial-refund fact is recorded via the audit log entry and the (P-905-reconciled) stock
movements, not via a sale-level status flag, since the current schema has no per-line status
column for `sale_items`. Update the handler: only call `sale.MarkRefunded()` when
`isWholeSaleRefund` is true.

## 4. TASK

1. Reconcile signatures against `docs/ARTIFACTS.md`, then create
   `RefundSaleCommand.cs`/handler in `StoreOS.Application/Sales/RefundSale/`, applying the
   documented correction (only transition `Status` to `Refunded` for a WHOLE-sale refund; a
   partial refund leaves `Status = Active` and relies on the audit trail for the record of what
   was returned).
2. Add `POST /api/sales/{id}/refund` to `SalesController.cs`.
3. Write `StoreOS.IntegrationTests/Sales/RefundSaleTests.cs`: (a) a WHOLE-sale refund
   (`SaleItemIds = null`) -> `Status` becomes `Refunded`, ALL physical lines' stock restored,
   original sale row otherwise unchanged; (b) a PARTIAL refund (one line out of two) -> `Status`
   REMAINS `Active`, ONLY the specified line's stock/IMEI is restored, the audit log records
   exactly which line ids were refunded; (c) `RefundAmount` exceeding `GrandTotal` ->
   `invalid_refund_amount`; (d) refunding an already-`Voided` sale -> `sale_not_active`; (e)
   customer ledger gets a `Refund` credit for the specified `RefundAmount` (which may be less
   than the full line value, e.g. a partial refund due to a restocking fee) and
   `total_purchases` decreases by exactly that amount, not the full original sale amount; (f) a
   caller without `sales:refund` -> 403 via live-DB check.

## 5. HARD CONSTRAINTS

- The partial-vs-whole refund status handling is a DELIBERATE, tested
  design decision — a partial refund never flips `sale.Status` away from `Active`, since
  Requirements' 3-value status enum has no partial-refund state; the audit log is the source of
  truth for exactly what was refunded in a partial case.
- The original `Sale` row's financial totals (`grand_total`, `taxable`, etc.) are NEVER
  rewritten by a refund, whole or partial — only `Status` (for whole-sale refunds) changes,
  preserving Invariant 6 identically to how void works.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RefundSaleTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-525 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-525 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-525` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RefundSaleCommand`/handler signatures, the
`POST /api/sales/{id}/refund` route, and the documented partial-vs-whole refund status
handling rule (critical for P-802's reporting to interpret correctly).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-525: <one-line summary>"` only after verification is
   fully green.
