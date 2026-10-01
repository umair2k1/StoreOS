# P-801 — Dashboard aggregate query (today's revenue, invoice count, profit, active repairs, low-stock, top-5, 7-day chart) via ReportingDbContext

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-801** of the StoreOS Cloud build ("Dashboard aggregate query (today's revenue, invoice count, profit, active repairs, low-stock, top-5, 7-day chart) via ReportingDbContext") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 7 (Repairs, P-701-708) fully complete. All prior phases (0-7)
  provide the complete transactional backbone this phase reports on.
- ReportingDbContext (P-119) exists and is proven, but only used so far by Inventory queries
  (P-404). This is the first DASHBOARD-specific usage.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Reporting/Dashboard/GetDashboardQuery.cs`:

```csharp
public record GetDashboardQuery(Guid[]? BranchIds) : IRequest<DashboardDto>;

public record DashboardDto(decimal TodayRevenue, int TodayInvoiceCount, decimal MonthlyProfit,
    int ActiveRepairsCount, int ReadyForPickupCount, IReadOnlyList<LowStockItemDto> LowStockItems,
    IReadOnlyList<TopSellingItemDto> TopSellingItems, IReadOnlyList<DailyRevenuePointDto> SevenDayRevenue);

public record LowStockItemDto(Guid ProductId, string Name, int StockQty, int Threshold);
public record TopSellingItemDto(string ItemName, int TotalQtySold);
public record DailyRevenuePointDto(DateOnly Date, decimal Revenue);
```

Implement EVERY metric exactly per Requirements Section 5, using `ReportingDbContext`:

**Today's revenue** (Section 5.2): `SUM(sales.grand_total) WHERE invoice_type = 'TaxInvoice'
AND status = 'Active' AND sale_date` falls within "today" in the tenant's business-day
definition (reuse the `Asia/Kolkata`-only simplification documented in P-521, throwing
`unsupported_timezone` for any other configured tenant timezone). EXCLUDES Voided, Refunded,
and Estimate invoices explicitly - write the query with all three exclusions spelled out, not
relying on a single combined flag.

**Today's invoice count**: same filter, `COUNT(*)`.

**Monthly profit** (Section 5.2): `Revenue - COGS - OperationalExpenses` for the current
calendar month. Revenue = `SUM(sale_items.taxable + sale_items.cgst + sale_items.sgst)` (i.e.
the line's contribution to grand total, excluding gift lines per Requirements Section 6.8) for
Active TaxInvoices this month. COGS = `SUM(sale_items.qty * sale_items.purchase_price_snapshot)`
for the SAME set of lines - explicitly using the HISTORICAL SNAPSHOT
(`purchase_price_snapshot`), NEVER the current `products.purchase_price`, per Requirements
Section 5.2's explicit warning ("not accidentally use current inventory purchase prices for
historical sales when historical purchase cost snapshots are available") and Invariant 4.
OperationalExpenses = `SUM(expenses.amount)` for the current month (reusing P-609's `Expense`
entity), matched to the same `BranchIds` filter when provided.

**Active repairs** (Section 5.2): `COUNT(*) FROM repair_jobs WHERE status NOT IN ('Delivered',
'Cancelled')` (reuse `RepairStatus.IsActive` conceptually, translated to SQL). Gated: if the
repairs feature is disabled for this tenant (P-708's `IFeatureManager`), return 0 for both
`ActiveRepairsCount` and `ReadyForPickupCount` rather than omitting the fields (keeps the DTO
shape stable for the frontend regardless of feature state).

**Ready for pickup**: `COUNT(*) WHERE status = 'ReadyForPickup'`.

**Low-stock alerts** (Section 5.3): reuse `ValuationCalculator.IsLowStock` (P-205) logic
translated to SQL (`stock_qty > 0 AND stock_qty <= low_stock_threshold`), EXCLUDING
RepairService category (already impossible since services never get `branch_inventory` rows,
per Invariant 3, but filter defensively anyway).

**Top-selling items** (Section 5.4): top 5 sale-line item names by `SUM(qty)` for the CURRENT
MONTH, `WHERE is_gift = false` (Requirements Section 5.4's explicit gift exclusion), across
Active TaxInvoices only.

**Seven-day revenue chart** (Section 5.5): daily `SUM(grand_total)` for Active TaxInvoices,
grouped by business date, for the last 7 days including today, with ZERO-FILLED days that had
no sales (do not omit a day with no revenue - return an explicit `0` entry for it).

Add `GET /api/reports/dashboard?branchIds=` to a new `ReportsController.cs`.

## 4. TASK

1. Create `GetDashboardQuery.cs`, its handler, and all DTOs in
   `StoreOS.Application/Reporting/Dashboard/`, implementing every metric exactly as described,
   using `ReportingDbContext` throughout.
2. Create `ReportsController.cs` with `GET /api/reports/dashboard`.
3. Write `StoreOS.IntegrationTests/Reporting/DashboardTests.cs` with hand-seeded, precisely
   computed expected values for EVERY metric: (a) today's revenue correctly excludes a Voided
   sale, a Refunded sale, and an Estimate created today, counting only Active TaxInvoices; (b)
   monthly profit uses `purchase_price_snapshot`, proven by changing the PRODUCT's current
   purchase price AFTER the sale and confirming the dashboard's profit figure is UNCHANGED
   (still reflects the snapshot); (c) low-stock list matches `IsLowStock`'s exact boundary
   (qty<=threshold, qty>0); (d) top-5 sellers correctly excludes a gift line's quantity from the
   ranking; (e) the 7-day chart includes a zero-revenue day explicitly, not omitted; (f) active
   repairs / ready-for-pickup return 0 when the repairs feature is disabled, without erroring.

## 5. HARD CONSTRAINTS

- Monthly profit's COGS calculation MUST use `sale_items.purchase_price_snapshot`
  exclusively - never join back to `products.purchase_price` for this calculation. This is
  Requirements Section 5.2's explicit, named warning and Invariant 4's direct application to
  reporting.
- Today's revenue/invoice-count explicitly exclude Voided, Refunded, AND Estimate - write all
  three exclusions explicitly in the query rather than relying on a single derived flag, so the
  intent is unambiguous to a future reader.
- Top-sellers explicitly excludes gift lines (`is_gift = false`).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter DashboardTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-801 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-801 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-801` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `DashboardDto` full shape and the `GET /api/reports/dashboard` route,
with explicit confirmation every exclusion rule (Voided/Refunded/Estimate, gift lines, snapshot
purchase price) is implemented as specified.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-801: <one-line summary>"` only after verification is
   fully green.
