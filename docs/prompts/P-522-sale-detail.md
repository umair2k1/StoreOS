# P-522 — GET /sales/{id} detail endpoint

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-522** of the StoreOS Cloud build ("GET /sales/{id} detail endpoint") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-521 complete: sales list/search/filter/sort/pagination all work.
- No detail view endpoint exists yet — this closes the forward-reference left open by P-513's
  Created($"/api/sales/{result.SaleId}", ...) response.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Sales/GetSaleById/GetSaleByIdQuery.cs`:

```csharp
public record GetSaleByIdQuery(Guid Id) : IRequest<SaleDetailDto>;

public record SaleLineDto(Guid Id, string ItemName, string Category, int Qty, decimal PricePerUnit,
    decimal Taxable, decimal Cgst, decimal Sgst, bool IsMarginScheme, bool IsGift, string? Imei, string? HsnSnapshot);

public record PaymentRecordDto(Guid Id, decimal Amount, string PaymentMode, DateOnly PaymentDate);

public record SaleDetailDto(Guid Id, string InvoiceNumber, string InvoiceType, DateTimeOffset SaleDate,
    Guid? CustomerId, string CustomerName, string? CustomerGstin, decimal TotalTaxable, decimal TotalCgst,
    decimal TotalSgst, decimal GrandTotal, decimal Discount, decimal AmountPaid, decimal Balance,
    string Status, string? PaymentMode, string? FinanceProvider, string? FinancerName,
    decimal? DownPayment, string? EmiDetailsJson, string? GiftDetailsJson, string? Description,
    Guid? RepairJobId, Guid? ConvertedFromEstimateId, Guid? LinkedTaxInvoiceId,
    IReadOnlyList<SaleLineDto> Items, IReadOnlyList<PaymentRecordDto> Payments);
```

`GetSaleByIdHandler` loads the `Sale` with its `Items` navigation AND a separate query for
`CustomerPayments` (check `docs/ARTIFACTS.md` for whether `Sale` already exposes a `Payments`
navigation; if not, query `_db.Set<CustomerPayment>().Where(p => p.SaleId == id)` directly
rather than retrofitting a new navigation property). Throws
`DomainValidationException("sale_not_found", ...)` (map to 404 in
`ExceptionHandlingMiddleware`, add this new mapping) if not found or not accessible under RLS
or query filters.

Add `GET /api/sales/{id}` to `SalesController.cs`.

## 4. TASK

1. Create `GetSaleByIdQuery.cs`, its handler, and DTOs in
   `StoreOS.Application/Sales/GetSaleById/` exactly as shown.
2. Add `sale_not_found` -> 404 mapping to `ExceptionHandlingMiddleware`.
3. Add `GET /api/sales/{id}` to `SalesController.cs`.
4. Write `StoreOS.IntegrationTests/Sales/GetSaleByIdTests.cs`: (a) fetching a sale created via
   full checkout returns all fields correctly populated, including its line items and payment
   records; (b) fetching a nonexistent id -> 404 sale_not_found; (c) fetching a sale belonging
   to a different tenant or an inaccessible branch -> also 404, never a different status code
   (confirms RLS naturally produces "not found" rather than "forbidden," the correct
   anti-enumeration behavior); (d) a converted estimate's tax invoice correctly shows
   ConvertedFromEstimateId populated, and fetching the original estimate shows
   LinkedTaxInvoiceId populated.

## 5. HARD CONSTRAINTS

- A sale outside the caller's tenant/branch access must return 404,
  not 403 — this prevents an attacker from probing which sale IDs exist across tenant
  boundaries via a status-code side channel.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter GetSaleByIdTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-522 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-522 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-522` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `SaleDetailDto`/`SaleLineDto`/`PaymentRecordDto` shapes, the
`GET /api/sales/{id}` route, and the sale_not_found->404 mapping.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-522: <one-line summary>"` only after verification is
   fully green.
