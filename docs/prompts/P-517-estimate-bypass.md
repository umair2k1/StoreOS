# P-517 — Estimate invoice_type support in checkout (bypass stock/IMEI decrement + no ledger debit)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-517** of the StoreOS Cloud build ("Estimate invoice_type support in checkout (bypass stock/IMEI decrement + no ledger debit)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-510-516 complete: checkout fully handles TaxInvoice sales including gifts,
  discounts, finance.
- `CheckoutHandler` (P-510) ALREADY guards the stock/IMEI reservation loop with
  `if (request.InvoiceType == InvoiceType.TaxInvoice)`, meaning Estimates should already skip
  it — this prompt is primarily a THOROUGH TEST + verification pass to prove Invariant 9
  ("Estimates are not finalized sales") holds completely, plus closing any remaining gaps
  (e.g. the customer-ledger debit posting must ALSO be skipped for estimates, and the current
  handler code needs to be checked for this).

## 3. CONTEXT BLOCK

Review `docs/ARTIFACTS.md`'s current `CheckoutHandler.Handle` record. Confirm
the customer-ledger-posting block:
```csharp
if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
{
    await _ledgerService.RecordInvoiceDebitAsync(...);
    ...
    customer.AddToTotalPurchases(grandTotal);
}
```
ALREADY correctly guards on `InvoiceType.TaxInvoice` (per the P-510 CONTEXT BLOCK draft) — if
your actual P-510 implementation differs (e.g. this guard was accidentally dropped during the
P-514/P-515 edits), FIX it now so estimates NEVER post to `customer_ledger_entries` and NEVER
increment `customers.total_purchases`.

Also confirm the credit-limit check itself
(`if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice) await
_creditLimitChecker.CheckAsync(...)`) is similarly guarded — an Estimate should NEVER be
blocked by a credit limit check, since it creates no actual debt (Requirements §17.5:
"Estimates must not create customer credit").

**Estimate numbering (corrected, DECISIONS D-07):** an Estimate is NOT a tax document, so it must NOT consume the tax-invoice series — a consumed number that never becomes a tax invoice would leave a gap in the GST Rule 46(b) consecutive series. Allocate estimate numbers from a SEPARATE per-branch series: `series_key = "EST-" + <fy key>` (P-501) and format them with `InvoiceNumberFormatter.FormatEstimate(branchCode, fyLabel, seq)` → `"B01/E/26-27/000007"` (no 16-character limit applies; the shared `UNIQUE (tenant_id, invoice_number)` index still holds). In `CheckoutHandler`/`CreateEstimate` choose the series key and formatter by `request.InvoiceType`. Tests: (1) creating an estimate does not change the tax series' `last_seq`; (2) a tax invoice created after three estimates is still `.../000001`; (3) converting an estimate allocates the NEXT tax number and leaves the estimate's own number untouched.

## 4. TASK

1. Read the CURRENT, ACTUAL `CheckoutHandler.Handle` source code directly from the
   repository (not just `docs/ARTIFACTS.md`'s possibly-stale summary) and verify BOTH the
   stock/IMEI-reservation guard AND the customer-ledger-posting guard are correctly conditioned
   on `request.InvoiceType == InvoiceType.TaxInvoice`. Fix either guard if it was lost or
   weakened during P-514/P-515's edits.
2. Write `StoreOS.IntegrationTests/Sales/EstimateCheckoutTests.cs`: (a) an Estimate for
   physical stock items -> `branch_inventory.stock_qty` UNCHANGED after the estimate is
   created (zero movements created); (b) an Estimate referencing a SPECIFIC IMEI -> the IMEI
   remains `InStock` afterward (not marked `Sold`); (c) an Estimate for a registered customer
   with a nonzero `GrandTotal` and zero payments -> `customers.total_purchases` UNCHANGED, ZERO
   new `customer_ledger_entries` rows created; (d) an Estimate that would EXCEED a customer's
   credit limit if it were a real sale -> succeeds anyway (no credit check applied); (e) the
   Estimate DOES get a valid, unique `invoice_number` from the normal sequence; (f) an Estimate
   appears in `sales` with `status='Active'`, `invoice_type='Estimate'` — confirm dashboard-style
   aggregate queries (a simple ad-hoc `SUM(grand_total) WHERE invoice_type='Estimate'`) are
   NOT accidentally included when a later report explicitly filters `invoice_type =
   'TaxInvoice'` (this is really testing that the DISTINCTION is queryable, full dashboard
   exclusion logic itself is built in P-801).

## 5. HARD CONSTRAINTS

- This is the definitive, thorough test pass for Invariant 9 — every
  later reporting prompt (P-801 onward) MUST filter on `invoice_type = 'TaxInvoice'` AND
  `status = 'Active'` wherever "revenue" or "customer debt" is computed; this prompt's tests
  are the proof that the underlying data correctly supports that filtering.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter EstimateCheckoutTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-517 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-517 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-517` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: confirmation (with an actual code excerpt) that both the stock/IMEI-reservation
guard and the customer-ledger-posting guard in `CheckoutHandler` correctly condition on
`InvoiceType.TaxInvoice`, and that Estimates still receive valid unique invoice numbers.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-517: <one-line summary>"` only after verification is
   fully green.
