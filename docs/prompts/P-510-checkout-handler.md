# P-510 — CheckoutHandler full transaction assembly (steps 1-10 per plan §10.1)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-510** of the StoreOS Cloud build ("CheckoutHandler full transaction assembly (steps 1-10 per plan §10.1)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- ALL of P-406 (stock service), P-408 (IMEI service), P-501 (invoice sequence
  allocator), P-502 (Sale/SaleItem entities), P-504 (CheckoutCommand — FINAL shape, quote it
  verbatim from `docs/ARTIFACTS.md`), P-505 (PricingResolver), P-506/507 (Customer/ledger
  service), P-508 (CustomerPayment/PaymentComposer), P-509 (CreditLimitChecker), P-116
  (IAuditWriter) are complete and independently tested.
- THIS PROMPT is the central orchestration point that wires all of the above together into one
  real, callable `CheckoutHandler`. Read `docs/ARTIFACTS.md`'s records for EVERY one of the
  dependencies above before writing a single line — every method signature you call must match
  exactly what those prompts actually produced.
- This does NOT yet include: gift/discount full wiring (P-514 refines discount application),
  finance/EMI composition (P-515), estimate bypass logic (P-517), or the retry/idempotency
  wrapper (P-511) — this prompt builds the CORE happy-path transaction assembly for a
  TaxInvoice with simple cash/credit payment, no discount, no finance, no gifts. Later prompts
  layer additional behavior onto this same handler.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `CheckoutHandler` no longer manages transactions or isolation at all: `TransactionBehavior` (P-109) already opened the transaction, `CheckoutCommand` is an `ISerializableRequest`, and the retry is `SerializationRetryBehavior` (P-511). Ignore every instruction below about 'escalating to Serializable', `BeginTransaction`, `SET LOCAL transaction_isolation` or Polly inside the handler. The handler must be stateless and free of non-database side effects because it can be re-executed.

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Sales/Checkout/CheckoutHandler.cs`
implementing the exact 10-step sequence from Plan §10.1:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tax;

namespace StoreOS.Application.Sales.Checkout;

public class CheckoutHandler : IRequestHandler<CheckoutCommand, CheckoutResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly PricingResolver _pricingResolver;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly CreditLimitChecker _creditLimitChecker;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;
    private readonly IAuditWriter _audit;

    public CheckoutHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext,
        PricingResolver pricingResolver, IInventoryStockService stockService,
        ICustomerLedgerService ledgerService, CreditLimitChecker creditLimitChecker,
        IInvoiceSequenceAllocator sequenceAllocator, IAuditWriter audit)
    {
        _db = db; _scope = scope; _branchContext = branchContext; _pricingResolver = pricingResolver;
        _stockService = stockService; _ledgerService = ledgerService; _creditLimitChecker = creditLimitChecker;
        _sequenceAllocator = sequenceAllocator; _audit = audit;
    }

    public async Task<CheckoutResult> Handle(CheckoutCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value;
        var saleDate = DateTimeOffset.UtcNow;
        var businessDate = DateOnly.FromDateTime(saleDate.UtcDateTime); // simplified; tenant-timezone conversion refined in reporting phase

        // Step 2: resolve pricing for every line (server-side, ignoring client totals)
        var resolvedLines = new List<ResolvedCartLine>();
        foreach (var line in request.Lines)
            resolvedLines.Add(await _pricingResolver.ResolveAsync(branchId, line, ct));

        var subtotal = resolvedLines.Sum(l => l.PricePerUnit * l.Qty);
        var taxableBeforeDiscount = resolvedLines.Sum(l => l.LineTaxable);
        var cgstBeforeDiscount = resolvedLines.Sum(l => l.LineCgst);
        var sgstBeforeDiscount = resolvedLines.Sum(l => l.LineSgst);

        // Discount application happens per-line via DiscountEngine — see P-514 for the full
        // per-line proportional wiring; this handler already calls it here since discount is
        // part of the core 10-step sequence (Plan §10.1 step 2 includes tax/discount together):
        decimal totalTaxable = taxableBeforeDiscount, totalCgst = cgstBeforeDiscount, totalSgst = sgstBeforeDiscount;
        if (request.InvoiceDiscount > 0)
        {
            var beforeAgg = new LineTax(taxableBeforeDiscount, cgstBeforeDiscount, sgstBeforeDiscount);
            var afterAgg = StoreOS.Domain.Tax.DiscountEngine.ApplyProportionalDiscount(beforeAgg, subtotal, request.InvoiceDiscount);
            totalTaxable = afterAgg.Taxable; totalCgst = afterAgg.Cgst; totalSgst = afterAgg.Sgst;
        }
        var grandTotal = StoreOS.Domain.Common.Money.Round2(subtotal - request.InvoiceDiscount);

        // Step 3: credit validation
        Guid? customerId = request.CustomerId;
        var requestedPaid = request.Payments.Sum(p => p.Amount);
        var newCreditAmount = Math.Max(0, grandTotal - requestedPaid);
        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
            await _creditLimitChecker.CheckAsync(customerId.Value, newCreditAmount, ct);

        // Step 4: reserve invoice sequence
        var seriesKey = $"FY{StoreOS.Domain.Sales.InvoiceNumberFormatter.FinancialYearLabel(businessDate)}";
        var branch = await _db.Set<StoreOS.Domain.Tenancy.Branch>().FirstAsync(b => b.Id == branchId, ct);
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
        var invoiceNumber = StoreOS.Domain.Sales.InvoiceNumberFormatter.FormatFyConsecutive(
            branch.Code, StoreOS.Domain.Sales.InvoiceNumberFormatter.FinancialYearLabel(businessDate), seq);

        // Step 1/6: create the sale header
        var sale = request.InvoiceType == InvoiceType.TaxInvoice
            ? Sale.CreateTaxInvoice(_scope.TenantId, branchId, invoiceNumber, saleDate, customerId,
                request.WalkInCustomerName ?? "Walk-in Customer", null, totalTaxable, totalCgst, totalSgst,
                grandTotal, request.InvoiceDiscount, _scope.UserId, request.Description)
            : Sale.CreateEstimate(_scope.TenantId, branchId, invoiceNumber, saleDate, customerId,
                request.WalkInCustomerName ?? "Walk-in Customer", null, totalTaxable, totalCgst, totalSgst,
                grandTotal, request.InvoiceDiscount, _scope.UserId, request.Description);
        await _db.Set<Sale>().AddAsync(sale, ct);

        // Step 5: stock & IMEI reservation (skipped entirely for Estimate — see P-517)
        if (request.InvoiceType == InvoiceType.TaxInvoice)
        {
            foreach (var line in resolvedLines)
            {
                if (!StoreOS.Domain.Catalog.ProductCategory.IsPhysical(line.Category)) continue;
                if (line.ImeiRowId is not null)
                    await _stockService.SellImeiAsync(line.ImeiRowId.Value, sale.Id, Guid.Empty, ct); // saleItemId patched below
                else
                    await _stockService.ReserveAndDecrementStockAsync(branchId, line.ProductId, line.Qty,
                        StoreOS.Domain.Inventory.MovementType.Sale, "Sale", sale.Id.ToString(), ct);
            }
        }

        // Step 6 (continued): sale items
        foreach (var line in resolvedLines)
        {
            var item = SaleItem.Create(_scope.TenantId, sale.Id, line.ProductId, line.ProductName,
                line.Category, line.Qty, line.PricePerUnit, line.PurchasePriceSnapshot, line.HsnCode,
                line.IsMarginScheme, line.IsGift, line.LineTaxable, line.LineCgst, line.LineSgst, line.Imei);
            await _db.Set<SaleItem>().AddAsync(item, ct);
        }

        // Step 8: payments & ledger
        var (payments, totalPaid) = PaymentComposer.Compose(_scope.TenantId, sale.Id, request.Payments, businessDate, _scope.UserId);
        foreach (var payment in payments) await _db.Set<CustomerPayment>().AddAsync(payment, ct);
        sale.ApplyPayment(totalPaid);

        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
        {
            await _ledgerService.RecordInvoiceDebitAsync(customerId.Value, sale.Id, grandTotal, branchId, businessDate, ct);
            if (totalPaid > 0)
                await _ledgerService.RecordPaymentCreditAsync(customerId.Value, sale.Id, totalPaid, branchId, businessDate, ct);
            var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == customerId.Value, ct);
            customer.AddToTotalPurchases(grandTotal);
        }

        // Step 10: audit
        await _audit.WriteAsync(branchId, "sale.checkout", "Sale", sale.Id.ToString(),
            new { invoiceNumber, grandTotal, totalPaid, request.InvoiceType }, ct);

        await _db.SaveChangesAsync(ct);

        return new CheckoutResult(sale.Id, invoiceNumber, grandTotal, totalPaid,
            Math.Max(0, grandTotal - totalPaid), saleDate);
    }
}
```

**Known gap you must fix while implementing**: `SellImeiAsync(line.ImeiRowId.Value, sale.Id,
Guid.Empty, ct)` passes a placeholder `Guid.Empty` for `saleItemId` because the `SaleItem`'s id
isn't known until it's created a few lines later, but `SellImeiAsync` needs to run BEFORE stock
is considered committed. Resolve this properly: reorder so `SaleItem` entities are created
FIRST (assigning their own `Guid.NewGuid()` ids client-side, which EF supports since `Id` is
generated in the domain factory, not by the database), collect a
`Dictionary<CheckoutCartLine, Guid>` or simply zip `resolvedLines` with their newly-created
`SaleItem.Id` values, THEN do the stock/IMEI reservation loop referencing the now-known
`saleItem.Id`. Rewrite the handler body with sale-items created BEFORE the stock/IMEI
reservation loop, correctly threading each `SaleItem.Id` into its corresponding
`SellImeiAsync` call. This also matches Plan §10.1's step ordering more sensibly re-read
(steps are a guide, not a rigid literal order when a genuine data dependency requires
reordering) — implement the CORRECTED order and note in `docs/BUILD_LOG.md` that this fixes an
ordering issue in the plan's literal step numbering.

Also confirm `Sale.CreateTaxInvoice`/`Sale.CreateEstimate` factory signatures match what you
actually wrote in P-502 — since P-502 said "write Sale.cs yourself," reconcile this handler's
calls with your own P-502 signatures exactly (adjust parameter order/names here to match,
don't assume the prose above is byte-exact against your own P-502 output).

Wire `POST /api/pos/checkout` is NOT added in this prompt — that is P-513 (after P-511's
idempotency/retry wrapper is also in place, so the endpoint from the start has the required
`Idempotency-Key` behavior). Register `CheckoutHandler` implicitly via MediatR's assembly
scan (no manual registration needed beyond what P-114 already set up).

## 4. TASK

1. Reconcile every dependency call in the handler against the ACTUAL signatures
   recorded in `docs/ARTIFACTS.md` for P-406, P-408, P-501, P-502, P-505, P-507, P-508, P-509,
   P-116 — adjust parameter names/order as needed; do not assume the prose in this prompt is
   byte-exact.
2. Fix the `SellImeiAsync`/`saleItemId` ordering issue exactly as described: create
   `SaleItem` entities FIRST, then use their real ids in the subsequent stock/IMEI reservation
   loop.
3. Write the complete `CheckoutHandler.cs` in
   `StoreOS.Application/Sales/Checkout/`, implementing the corrected step order.
4. Write `StoreOS.IntegrationTests/Sales/CheckoutHandlerTests.cs` covering the CORE happy path
   only (later prompts add discount/gift/finance/estimate specific tests): (a) a simple
   single-line cash sale of a `NewPhone` with an IMEI -> creates `Sale`+`SaleItem`, decrements
   stock, marks the IMEI `Sold` with the CORRECT `sale_item_id` (not `Guid.Empty`), creates one
   `inventory_movements` row, allocates a valid FY-consecutive invoice number, `AmountPaid ==
   GrandTotal`, `Balance == 0`; (b) a credit sale (zero payments) for a registered customer ->
   `customer_ledger_entries` gets an `Invoice` debit, `customers.total_purchases` increases,
   `Sale.AmountPaid == 0`, `Balance == GrandTotal`; (c) a sale exceeding the customer's credit
   limit without override permission -> the WHOLE transaction rolls back (confirm via
   `dotnet` assertions that NO `Sale`/`SaleItem`/stock-movement rows persisted at all — the
   exception must propagate before `SaveChangesAsync`, and even IF it happened after, the
   ambient transaction from P-109 would roll everything back); (d) an invoice-level discount
   correctly reduces `GrandTotal` and proportionally shrinks `TotalCgst`/`TotalSgst` (reuse
   P-203's exact math, verify numerically); (e) an out-of-stock line -> the whole checkout
   fails atomically with nothing persisted.

## 5. HARD CONSTRAINTS

- The corrected step order (sale items created before stock/IMEI
  reservation) is MANDATORY — do not ship the placeholder-`Guid.Empty` version under any
  circumstance; a `sale_item_id` of all-zeros on a sold IMEI is a real, damaging data-integrity
  defect, not a cosmetic issue.
- This handler calls `_db.SaveChangesAsync(ct)` EXACTLY ONCE, at the very end — every internal
  service call before that point only STAGES changes (per their established conventions from
  P-116/P-406/P-408/P-507/P-508), guaranteeing the whole checkout commits or rolls back as one
  atomic unit (Invariant 11).
- Estimate-type stock/IMEI bypass is ALREADY partially wired in this handler (the `if
  (request.InvoiceType == InvoiceType.TaxInvoice)` guard around the reservation loop) — P-517
  refines and tests this further but the core skip-logic belongs here now, do not defer it
  entirely to P-517.

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
dotnet build StoreOS.sln
dotnet test backend/tests/StoreOS.IntegrationTests --filter CheckoutHandlerTests
# Expected: all 5 cases pass, especially (a)'s correct sale_item_id threading and (c)/(e)'s
# full-rollback atomicity.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-510 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-510 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-510` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the COMPLETE final `CheckoutHandler.Handle` method (verbatim, since P-511,
P-514, P-515, P-517, P-520 all extend or wrap THIS exact handler and need its precise current
shape), and explicit confirmation of the corrected sale-items-before-IMEI-reservation
ordering.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-510: <one-line summary>"` only after verification is
   fully green.
