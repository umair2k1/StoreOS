# P-802 — Daily Sales Audit report (sales summary, inventory summary, payment breakdown, category breakdown, itemized log)

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-802** of the StoreOS Cloud build ("Daily Sales Audit report (sales summary, inventory summary, payment breakdown, category breakdown, itemized log)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-801 complete: dashboard aggregates all work correctly with proper
  exclusion rules.
- No daily sales AUDIT report exists yet (Requirements Section 24) - a more detailed,
  point-in-time, printable report distinct from the live dashboard.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Reporting/DailySales/GetDailySalesReportQuery.cs`:

```csharp
public record GetDailySalesReportQuery(DateOnly Date, Guid[]? BranchIds) : IRequest<DailySalesReportDto>;

public record SalesSummaryDto(decimal Revenue, decimal BuyingCost, decimal Profit, decimal GstCollected,
    int InvoiceCount, int ItemsSold);
public record InventorySummaryDto(int TotalStockVolume, decimal StockWorthAtCost, decimal RetailValuation,
    int LowStockCount, int OutOfStockCount);
public record PaymentBreakdownDto(decimal Cash, decimal Upi, decimal Finance, decimal Other, decimal Total);
public record CategorySalesDto(string Category, decimal Revenue, int ItemsSold);
public record ItemizedSaleLineDto(string InvoiceNumber, DateOnly Date, string CustomerName,
    string ItemName, int Qty, decimal SellingValue, decimal CostValue, decimal Gst, string? Imei, string? PaymentMode);

public record DailySalesReportDto(DateOnly Date, SalesSummaryDto Sales, InventorySummaryDto Inventory,
    PaymentBreakdownDto Payments, IReadOnlyList<CategorySalesDto> ByCategory,
    IReadOnlyList<ItemizedSaleLineDto> ItemizedLog, decimal CollectedAmount, decimal OutstandingAmount);
```

Implement exactly per Requirements Section 24:

**Sales summary**: `Revenue = SUM(grand_total)` for Active TaxInvoices on `Date`; `BuyingCost =
SUM(qty * purchase_price_snapshot)`; `Profit = Revenue - BuyingCost`; `GstCollected =
SUM(cgst + sgst)`; `InvoiceCount`; `ItemsSold = SUM(qty)` excluding gift lines from the
sold-items COUNT (a gift is still physically given away, but Requirements Section 24's "Items
sold" in a REVENUE-adjacent summary should logically exclude non-revenue gift units - apply the
SAME gift exclusion used in P-801's top-sellers for consistency).

**Inventory summary** (Section 24, "Repair services must be excluded"): reuse
`ValuationCalculator` (P-205) exactly as P-404's endpoint does, filtered to the requested
branches, AS OF the report's run-time (current stock state, not a historical-as-of-that-date
snapshot - Requirements doesn't ask for historical inventory reconstruction, just "as of now"
alongside a specific day's SALES data).

**Payment breakdown**: `Cash`/`Upi`/`Finance` sum `grand_total` (or, more precisely, the
PAYMENT amounts actually recorded via `customer_payments.payment_mode`, which is more accurate
than `sales.payment_mode` for a partially-paid sale spanning multiple modes over time - use
`customer_payments` joined to sales on `Date`, summed by `payment_mode`) grouped into the four
buckets (`Cash`, `UPI`, `Finance`, everything else as `Other` including `Card`/`Credit`
payment-mode strings that don't fit the first three named buckets - actually Requirements
Section 24's exact bucket list is "Cash, UPI, Finance, Other" so `Card` and any `Credit`-mode
records ALSO fall into `Other`). `Total` is the sum of all four.

**The report must clearly distinguish** (Section 24): `Sale value` = `grand_total` sum (already
captured in `SalesSummaryDto.Revenue`), `Collected amount` = `SUM(customer_payments.amount)` for
payments recorded ON this date (not necessarily tied 1:1 to sales created that same day, since a
payment could be a later installment against an older sale - but for a DAILY report, "collected
today" means payments RECORDED today regardless of which day the original sale happened),
`Outstanding amount` = `Revenue - Collected` for TODAY's sales specifically (a stricter,
same-day interpretation for THIS specific summary field, separate from the payment-breakdown's
broader same-day-collected-regardless-of-sale-date figure - implement both interpretations
correctly and distinctly, documenting the difference clearly in a code comment since this is a
subtle but important distinction).

**Sales by category** (Section 24): group Active TaxInvoice line-item revenue by category, at
minimum covering all 4 (`NewPhone`, `UsedPhone`, `Accessory`, `RepairService`), zero-filled if a
category had no sales that day.

**Itemized sales log** (Section 24): every line item sold that day with full audit detail.

Add `GET /api/reports/daily-sales?date=&branchIds=` to `ReportsController.cs`.

## 4. TASK

1. Create `GetDailySalesReportQuery.cs`, handler, and all DTOs in
   `StoreOS.Application/Reporting/DailySales/`, implementing every section exactly as described,
   using `ReportingDbContext`.
2. Add the route to `ReportsController.cs`.
3. Write `StoreOS.IntegrationTests/Reporting/DailySalesReportTests.cs`: seed a full day's mixed
   activity (multiple invoices across categories, a gift line, a voided sale that should be
   excluded, payments across different modes, a partial-payment credit sale) and assert EVERY
   section of the report against hand-computed exact values: (a) sales summary profit uses
   snapshot cost; (b) inventory summary excludes RepairService; (c) payment breakdown correctly
   buckets Card/Credit into Other; (d) the sale-value vs. collected vs. outstanding distinction
   is correct per the documented dual interpretation; (e) category breakdown zero-fills a
   category with no sales; (f) itemized log includes every line with correct IMEI/payment-mode
   detail.

## 5. HARD CONSTRAINTS

- COGS/profit calculations use `purchase_price_snapshot` exclusively, never
  current `products.purchase_price` - identical rule to P-801, re-verified here for the daily
  report specifically.
- The "collected amount" (broader, any payment recorded today) vs. "outstanding for today's
  sales" (narrower, same-day-sale-specific) distinction must be implemented and tested exactly
  as documented - these are deliberately different, subtly-named figures serving different
  audit purposes.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter DailySalesReportTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-802 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-802 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-802` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete `DailySalesReportDto` shape (all six nested DTOs) and the
`GET /api/reports/daily-sales` route, with the documented collected-vs-outstanding dual
interpretation.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-802: <one-line summary>"` only after verification is
   fully green.
