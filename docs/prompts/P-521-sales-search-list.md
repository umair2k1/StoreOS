# P-521 — GET /sales + filters/sort/pagination

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-521** of the StoreOS Cloud build ("GET /sales + filters/sort/pagination") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-520 complete: full checkout + estimate-conversion lifecycle works.
- No sales-history LIST endpoint exists yet. This implements Requirements Section 16.2-16.6 in full.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Sales/Search/GetSalesQuery.cs`
covering EVERY filter from Requirements Section 16.2-16.6:

```csharp
public record GetSalesQuery(
    string? Search,
    string? DateFilter,
    DateOnly? CustomFrom, DateOnly? CustomTo,
    string? Status, string? PaymentMode, string? InvoiceType,
    bool? PaidOnly, bool? DueOnly, bool? FinancedOnly,
    string? Category, decimal? MinAmount, decimal? MaxAmount,
    string SortBy = "SaleDate", string SortDirection = "desc",
    int Page = 1, int PageSize = 50
) : IRequest<PagedResult<SaleSummaryDto>>;

public record SaleSummaryDto(Guid Id, string InvoiceNumber, DateTimeOffset SaleDate,
    string CustomerName, decimal GrandTotal, decimal AmountPaid, decimal Balance,
    string Status, string InvoiceType, string? PaymentMode, bool IsFinanced);
```

Search matches invoice number, customer name, customer GSTIN, item names, IMEIs, finance
provider, financer name, and description, per Requirements Section 16.2. DateFilter accepts:
Today, Yesterday, ThisWeek, ThisMonth, LastMonth, ThisYear, Custom. SortBy accepts: SaleDate,
GrandTotal, InvoiceNumber, CustomerName, Balance.

`GetSalesHandler` builds the query INCREMENTALLY, applying each filter conditionally (only
`.Where(...)` when the corresponding parameter is non-null), using `ReportingDbContext`
(read-heavy per Plan Section 11 routing). The `Search` free-text filter needs a join to
`sale_items` for item-name/IMEI matching — use an `Any(...)` predicate against a joined
`sale_items` set:

```csharp
if (!string.IsNullOrWhiteSpace(request.Search))
{
    var term = $"%{request.Search}%";
    query = query.Where(s =>
        EF.Functions.ILike(s.InvoiceNumber, term) ||
        EF.Functions.ILike(s.CustomerName ?? "", term) ||
        EF.Functions.ILike(s.CustomerGstin ?? "", term) ||
        EF.Functions.ILike(s.FinanceProvider ?? "", term) ||
        EF.Functions.ILike(s.FinancerName ?? "", term) ||
        EF.Functions.ILike(s.Description ?? "", term) ||
        _reportingDb.Set<SaleItem>().Any(si => si.SaleId == s.Id &&
            (EF.Functions.ILike(si.ItemName, term) || EF.Functions.ILike(si.Imei ?? "", term))));
}
```

Date filters translate to explicit UTC range boundaries computed server-side using the
tenant's timezone (Plan Section 6.6). Read `tenants.timezone` (P-102) and use .NET's built-in
`TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")` (supported on Linux containers via OS
tzdata — verify this works in the build environment and note if it does not). This initial
release supports ONLY `"Asia/Kolkata"` as a tenant timezone; if a tenant's `timezone` value is
ever anything else, throw `DomainValidationException("unsupported_timezone", ...)` rather than
silently miscalculating date boundaries. Document this as a known simplification consistent
with the product's explicit India focus (Requirements Section 37).

`PaidOnly`/`DueOnly` translate to `AmountPaid >= GrandTotal` / `AmountPaid < GrandTotal`.
`FinancedOnly` translates to `PaymentMode == "Finance"`. `Category` filters via the same
`sale_items` join pattern used for search. Sorting applies a switch on `SortBy` mapping to the
correct `OrderBy`/`OrderByDescending` expression, with `Balance` computed as
`GrandTotal - AmountPaid` as a translatable SQL arithmetic expression.

Create `SalesController.cs`: `GET /api/sales` mapping query-string parameters to
`GetSalesQuery`.

## 4. TASK

1. Create `GetSalesQuery.cs`, its handler, and `SaleSummaryDto` in
   `StoreOS.Application/Sales/Search/`, implementing every filter and sort option listed,
   using `ReportingDbContext`.
2. Create `SalesController.cs` with `GET /api/sales`.
3. Write `StoreOS.IntegrationTests/Sales/GetSalesTests.cs` with at least one test per filter
   dimension from Requirements Section 16.2-16.6: search by invoice number; search by IMEI;
   search by finance provider; each of the 7 date-filter options including custom range; each
   status value; each payment mode; each invoice type; paid vs due; financed vs standard;
   category filter; min/max amount range; each of the 5 sort fields in both directions;
   pagination page-size boundaries. This will be a large test file. Every test must use
   precise, hand-seeded data with a known expected result set, never a vague "returns
   something" assertion.

## 5. HARD CONSTRAINTS

- EVERY filter dimension from Requirements Section 16.2-16.6 must be
  implemented — this endpoint is explicitly called out as needing every filter combination
  tested; do not ship a partial implementation covering only some filters.
- All filtering/sorting/pagination happens at the SQL level via `ReportingDbContext` — never
  materialize the full table and filter/sort in memory.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter GetSalesTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-521 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-521 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-521` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete `GetSalesQuery`/`SaleSummaryDto` shapes and the `GET /api/sales`
route with its full query-string parameter list.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-521: <one-line summary>"` only after verification is
   fully green.
