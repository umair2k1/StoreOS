# P-520 — POST /pos/estimate/{id}/convert handler (plan §10.7 steps 1-11)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-520** of the StoreOS Cloud build ("POST /pos/estimate/{id}/convert handler (plan §10.7 steps 1-11)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-519 complete: bidirectional linkage columns/methods exist.
- ALL of checkout's machinery (P-501 sequence allocator, P-406/408 stock/IMEI services, P-507
  ledger service, P-509 credit checker, P-511 idempotency/retry) is available for reuse. This
  is the second most complex handler in the codebase after `CheckoutHandler` itself, and
  deliberately REUSES as much of that machinery as possible rather than re-implementing
  parallel logic.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `ConvertEstimateCommand` is an `ISerializableRequest` and an `IIdempotentRequest` (scope `pos.estimate.convert:{EstimateId}`, required). Retry and transactions come from P-109/P-511; the handler contains neither.

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Sales/ConvertEstimate/ConvertEstimateCommand.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.ConvertEstimate;

public record ConvertEstimateCommand(Guid EstimateId, IReadOnlyList<Checkout.CheckoutPayment> Payments)
    : IRequest<ConvertEstimateResult>, IRequiresPermission, INeedsSerializationRetry
{
    public string RequiredPermission => "pos:checkout";
}

public record ConvertEstimateResult(Guid NewSaleId, string InvoiceNumber, decimal GrandTotal,
    decimal AmountPaid, decimal Balance);
```

Create `ConvertEstimateHandler.cs` implementing Plan §10.7's exact 11 steps:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.ConvertEstimate;

public class ConvertEstimateHandler : IRequestHandler<ConvertEstimateCommand, ConvertEstimateResult>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IBranchContext _branchContext;
    private readonly IInventoryStockService _stockService;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly CreditLimitChecker _creditLimitChecker;
    private readonly IInvoiceSequenceAllocator _sequenceAllocator;
    private readonly IAuditWriter _audit;

    public ConvertEstimateHandler(IAppDbContext db, IAccessScope scope, IBranchContext branchContext,
        IInventoryStockService stockService, ICustomerLedgerService ledgerService,
        CreditLimitChecker creditLimitChecker, IInvoiceSequenceAllocator sequenceAllocator, IAuditWriter audit)
    {
        _db = db; _scope = scope; _branchContext = branchContext; _stockService = stockService;
        _ledgerService = ledgerService; _creditLimitChecker = creditLimitChecker;
        _sequenceAllocator = sequenceAllocator; _audit = audit;
    }

    public async Task<ConvertEstimateResult> Handle(ConvertEstimateCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value;

        // Step 2: load estimate, verify type/tenant/branch access
        var estimate = await _db.Set<Sale>()
            .Include(s => s.Items) // adjust to your actual Sale.Items navigation from P-502
            .FirstOrDefaultAsync(s => s.Id == request.EstimateId, ct)
            ?? throw new DomainValidationException("estimate_not_found", "Estimate not found.");
        if (estimate.InvoiceType != InvoiceType.Estimate)
            throw new DomainValidationException("not_an_estimate", "Sale is not an estimate.");
        if (estimate.LinkedTaxInvoiceId is not null)
            throw new DomainValidationException("estimate_already_converted", "This estimate has already been converted.");
        if (estimate.BranchId != branchId)
            throw new DomainValidationException("branch_mismatch", "Estimate belongs to a different branch.");

        // Step 3: verify current stock/IMEI availability for every physical line
        foreach (var item in estimate.Items.Where(i => StoreOS.Domain.Catalog.ProductCategory.IsPhysical(i.Category)))
        {
            if (!string.IsNullOrEmpty(item.Imei))
                await _stockService.LoadImeiForSaleAsync(branchId, item.Imei, ct); // throws if unavailable
            else
            {
                var available = await _db.Set<StoreOS.Domain.Inventory.BranchInventory>()
                    .Where(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId && bi.ProductId == item.ProductId)
                    .Select(bi => bi.StockQty).FirstOrDefaultAsync(ct);
                if (available < item.Qty)
                    throw new DomainValidationException("insufficient_stock",
                        $"Insufficient stock for '{item.ItemName}' to convert this estimate.");
            }
        }

        // Step 4: credit validation if applicable
        var requestedPaid = request.Payments.Sum(p => p.Amount);
        var newCreditAmount = Math.Max(0, estimate.GrandTotal - requestedPaid);
        if (estimate.CustomerId is not null)
            await _creditLimitChecker.CheckAsync(estimate.CustomerId.Value, newCreditAmount, ct);

        // Step 5: reserve new tax invoice number
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var seriesKey = $"FY{StoreOS.Domain.Sales.InvoiceNumberFormatter.FinancialYearLabel(businessDate)}";
        var branch = await _db.Set<StoreOS.Domain.Tenancy.Branch>().FirstAsync(b => b.Id == branchId, ct);
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
        var invoiceNumber = StoreOS.Domain.Sales.InvoiceNumberFormatter.FormatFyConsecutive(
            branch.Code, StoreOS.Domain.Sales.InvoiceNumberFormatter.FinancialYearLabel(businessDate), seq);

        // Step 6/7: create the new tax invoice sale + items (copying line data from the estimate)
        var newSale = Sale.CreateTaxInvoice(_scope.TenantId, branchId, invoiceNumber, DateTimeOffset.UtcNow,
            estimate.CustomerId, estimate.CustomerName, estimate.CustomerGstin, estimate.TotalTaxable,
            estimate.TotalCgst, estimate.TotalSgst, estimate.GrandTotal, estimate.Discount, _scope.UserId,
            estimate.Description);
        newSale.MarkConvertedFrom(estimate.Id);
        await _db.Set<Sale>().AddAsync(newSale, ct);

        foreach (var item in estimate.Items)
        {
            var newItem = StoreOS.Domain.Sales.SaleItem.Create(_scope.TenantId, newSale.Id, item.ProductId,
                item.ItemName, item.Category, item.Qty, item.PricePerUnit, item.PurchasePriceSnapshot,
                item.HsnSnapshot, item.IsMarginScheme, item.IsGift, item.Taxable, item.Cgst, item.Sgst, item.Imei);
            await _db.Set<SaleItem>().AddAsync(newItem, ct);

            if (StoreOS.Domain.Catalog.ProductCategory.IsPhysical(item.Category) && !item.IsGift)
            {
                if (!string.IsNullOrEmpty(item.Imei))
                {
                    var snapshot = await _stockService.LoadImeiForSaleAsync(branchId, item.Imei, ct);
                    await _stockService.SellImeiAsync(snapshot.Id, newSale.Id, newItem.Id, ct);
                }
                else
                {
                    await _stockService.ReserveAndDecrementStockAsync(branchId, item.ProductId!.Value, item.Qty,
                        StoreOS.Domain.Inventory.MovementType.Sale, "Sale", newSale.Id.ToString(), ct);
                }
            }
        }

        // Step 9: payments + ledger
        var (payments, totalPaid) = PaymentComposer.Compose(_scope.TenantId, newSale.Id, request.Payments, businessDate, _scope.UserId);
        foreach (var p in payments) await _db.Set<CustomerPayment>().AddAsync(p, ct);
        newSale.ApplyPayment(totalPaid);
        if (estimate.CustomerId is not null)
        {
            await _ledgerService.RecordInvoiceDebitAsync(estimate.CustomerId.Value, newSale.Id, estimate.GrandTotal, branchId, businessDate, ct);
            if (totalPaid > 0)
                await _ledgerService.RecordPaymentCreditAsync(estimate.CustomerId.Value, newSale.Id, totalPaid, branchId, businessDate, ct);
            var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == estimate.CustomerId.Value, ct);
            customer.AddToTotalPurchases(estimate.GrandTotal);
        }

        // Step 10: update the original estimate
        estimate.MarkConvertedToTaxInvoice(newSale.Id);

        // Step 11: audit
        await _audit.WriteAsync(branchId, "estimate.converted", "Sale", newSale.Id.ToString(),
            new { EstimateId = estimate.Id, invoiceNumber }, ct);

        await _db.SaveChangesAsync(ct);

        return new ConvertEstimateResult(newSale.Id, invoiceNumber, estimate.GrandTotal, totalPaid,
            Math.Max(0, estimate.GrandTotal - totalPaid));
    }
}
```

Reconcile every call above against your ACTUAL P-502/P-505/P-507/P-508/P-509 signatures
(especially `Sale.Items` navigation property name/shape, and `Sale.CreateTaxInvoice`'s exact
parameter list) before finalizing — adjust as needed, this is a synthesis prompt, not a
byte-exact copy target.

Add `POST /api/pos/estimate/{id}/convert` to `PosController.cs`, REQUIRING `Idempotency-Key`
(same mandatory-header pattern as checkout, per Plan §10.7's explicit idempotency requirement,
since this DOES have real financial/stock consequences unlike plain estimate creation).

## 4. TASK

1. Reconcile and write the complete `ConvertEstimateCommand.cs`/
   `ConvertEstimateHandler.cs` in `StoreOS.Application/Sales/ConvertEstimate/`, matching your
   actual prior prompts' signatures.
2. Add the `POST /api/pos/estimate/{id}/convert` controller action requiring
   `Idempotency-Key` (400 if absent, same pattern as P-513's checkout action).
3. Write `StoreOS.IntegrationTests/Sales/ConvertEstimateTests.cs`: (a) full happy path:
   create an estimate, convert it -> new `TaxInvoice` sale created with a valid new invoice
   number, stock/IMEI correctly decremented NOW (they weren't at estimate-creation time per
   P-517), bidirectional links (`converted_from_estimate_id`/`linked_tax_invoice_id`) both set
   correctly; (b) converting the SAME estimate TWICE -> second attempt throws
   `estimate_already_converted`; (c) if stock was SOLD OUT by another sale between estimate
   creation and conversion attempt -> `insufficient_stock`, estimate remains un-converted,
   nothing persisted; (d) if the specific IMEI on the estimate was sold to someone else in the
   meantime -> conversion fails cleanly (via `LoadImeiForSaleAsync`'s existing guard); (e)
   idempotency: converting with the same `Idempotency-Key` twice returns the cached result
   without double-converting; (f) attempting to convert a regular `TaxInvoice` (not an
   Estimate) -> `not_an_estimate`.

## 5. HARD CONSTRAINTS

- The conversion handler REUSES `IInventoryStockService`,
  `ICustomerLedgerService`, `CreditLimitChecker`, and `InvoiceSequenceAllocator` — it does NOT
  reimplement parallel stock/ledger/sequence logic. If you find yourself writing new stock-
  mutation SQL here, stop and use the existing service instead.
- This command is marked `INeedsSerializationRetry` (P-511) just like `CheckoutCommand`, since
  it has the identical class of concurrency exposure (stock/IMEI availability re-validated at
  conversion time, same race potential as checkout itself).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ConvertEstimateTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-520 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-520 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-520` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete `ConvertEstimateCommand`/`ConvertEstimateHandler` signatures, the
`POST /api/pos/estimate/{id}/convert` route (mandatory Idempotency-Key), and confirmation the
handler reuses (not reimplements) the checkout-phase's core services.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-520: <one-line summary>"` only after verification is
   fully green.
