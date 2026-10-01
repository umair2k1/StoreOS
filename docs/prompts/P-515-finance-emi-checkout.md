# P-515 — Finance/EMI fields in checkout (finance provider, down payment, EMI calc, tenure)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-515** of the StoreOS Cloud build ("Finance/EMI fields in checkout (finance provider, down payment, EMI calc, tenure)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-510–514 complete: full checkout with gift/discount correctly wired.
- `FinanceEngine.CalculatePrincipal`/`CalculateEmi` (P-204) exist but are not yet called from
  checkout. `CheckoutCommand.Finance` (a `CheckoutFinanceDetails?` field, P-504) exists but is
  currently ignored by `CheckoutHandler`.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Sales/Checkout/FinanceComposer.cs`:

```csharp
using StoreOS.Domain.Finance;

namespace StoreOS.Application.Sales.Checkout;

public record ComposedFinance(string FinanceProvider, string? FinancerName, decimal DownPayment,
    int TenureMonths, decimal AnnualInterestRate, decimal Principal, decimal MonthlyEmi, decimal TotalPayable);

public static class FinanceComposer
{
    public static ComposedFinance Compose(CheckoutFinanceDetails details, decimal grandTotal)
    {
        var principal = FinanceEngine.CalculatePrincipal(grandTotal, details.DownPayment);
        var emi = FinanceEngine.CalculateEmi(principal, details.AnnualInterestRate, details.TenureMonths);
        var totalPayable = StoreOS.Domain.Common.Money.Round2(details.DownPayment + emi * details.TenureMonths);
        return new ComposedFinance(details.FinanceProvider, details.FinancerName, details.DownPayment,
            details.TenureMonths, details.AnnualInterestRate, principal, emi, totalPayable);
    }
}
```

Update `CheckoutHandler.Handle` (extend the P-510/P-514 implementation, do not rewrite from
scratch): when `request.Finance is not null`, call `FinanceComposer.Compose(request.Finance,
grandTotal)`, and persist the result onto the `Sale` entity. This requires `Sale` (P-502) to
have a method for recording finance details — add `Sale.RecordFinanceDetails(string
financeProvider, string? financerName, decimal downPayment, string emiDetailsJson)` if not
already present (check `docs/ARTIFACTS.md`'s P-502 record; if the method doesn't exist, add it
now via a small, explicit extension since `Sale`'s properties are private-set):

```csharp
// Addition to Sale.cs if not already present:
public void RecordFinanceDetails(string financeProvider, string? financerName, decimal downPayment, string emiDetailsJson)
{
    PaymentMode = StoreOS.Domain.Sales.PaymentMode.Finance;
    FinanceProvider = financeProvider;
    FinancerName = financerName;
    DownPayment = downPayment;
    EmiDetailsJson = emiDetailsJson;
}
```

`emiDetailsJson` is a `System.Text.Json.JsonSerializer.Serialize(composedFinance)` call storing
the FULL `ComposedFinance` record (principal, EMI, tenure, rate, total payable) for invoice
display and audit purposes. In `CheckoutHandler`, the down payment amount should also be
treated as an ACTUAL payment toward the sale (added to the `Payments` composition, or applied
directly via `sale.ApplyPayment(downPayment)` if the down payment isn't ALSO separately listed
in `request.Payments` — clarify: the down payment IS the customer's immediate payment; the
financed remainder (`principal`) is NOT collected now, it's paid over time to the finance
provider, which is OUTSIDE this system's cash flow (StoreOS just records the arrangement for
invoice/reporting purposes) — so `sale.ApplyPayment(downPayment)` directly, and the sale's
`Balance` (GrandTotal - AmountPaid) will show the FINANCED portion as "outstanding" from
StoreOS's perspective even though it's not customer credit risk in the traditional sense, it's
the finance company's portion. Do NOT ALSO create a `customer_ledger_entries` debit for this
outstanding balance if `request.Finance is not null` (a financed sale's "balance" is not
customer credit exposure - the finance company owes the shop, not the customer) — add this
exception to the ledger-posting logic: only post an `Invoice` debit to customer ledger for
UNFINANCED credit sales, skip it when `Finance is not null` for the unpaid remainder specifically
(the down payment portion, if any, is still recorded as a normal payment).

## 4. TASK

1. Create `FinanceComposer.cs`/`ComposedFinance` exactly as shown in
   `StoreOS.Application/Sales/Checkout/`.
2. Extend `Sale.cs` with `RecordFinanceDetails` if not already present (check
   `docs/ARTIFACTS.md` first).
3. Extend `CheckoutHandler.Handle`: when `Finance` is present, compose it, call
   `sale.RecordFinanceDetails(...)`, apply the down payment as a real payment
   (`sale.ApplyPayment(details.DownPayment)` plus a corresponding `CustomerPayment` row if a
   customer is attached, using the SAME `PaymentComposer`-style creation), and SKIP the
   customer-ledger `Invoice` debit for the financed remainder (per the reasoning above) while
   STILL posting a `Payment` credit for the down payment portion if a customer is attached
   (so the ledger correctly shows what the CUSTOMER actually paid, even on a financed sale).
4. Write `StoreOS.IntegrationTests/Sales/FinanceCheckoutTests.cs`: (a) a financed sale with
   `AnnualInterestRate=0`, `TenureMonths=12`, `DownPayment=2000` on a `GrandTotal=14000` sale ->
   `Principal=12000`, `MonthlyEmi=1000.00` (exact, per P-204's zero-interest case),
   `Sale.PaymentMode == Finance`, `Sale.AmountPaid == 2000` (just the down payment),
   `emi_details` JSON round-trips correctly with all composed fields; (b) same but with
   `AnnualInterestRate=12` -> EMI matches P-204's positive-interest hand-computed value exactly;
   (c) a financed sale for a registered customer -> NO `Invoice` debit posted to
   `customer_ledger_entries` for the financed remainder, but the down payment portion DOES
   appear as a `Payment` credit-shaped... wait, re ledger semantics: a "Payment" entry TYPE is
   always a CREDIT (reduces balance) — but if there's no corresponding INVOICE DEBIT for a
   financed sale, a lone payment credit with no debit would make the customer's balance go
   NEGATIVE, which `GetConsolidatedBalanceAsync`'s `Math.Max(0, ...)` guard would clamp to 0,
   silently losing information. Resolve this correctly: for a financed sale, do NOT post EITHER
   a debit OR a credit to `customer_ledger_entries` — financing is entirely outside the
   customer-ledger/credit-limit system (Requirements don't model financed balances as customer
   credit exposure at all). Update the handler accordingly: skip BOTH the invoice debit AND any
   payment credit to `customer_ledger_entries` when `Finance is not null`, while STILL recording
   the actual `CustomerPayment` row (down payment) against the sale itself (for payment-history/
   audit purposes on the SALE record, just not reflected in the customer's separate credit
   ledger); (d) `DownPayment > GrandTotal` -> `finance_down_payment_exceeds_total` surfaces
   cleanly as a 422 from checkout (proving `FinanceEngine`'s own guard propagates correctly
   through the full stack).

## 5. HARD CONSTRAINTS

- A financed sale's remainder is NEVER reflected in
  `customer_ledger_entries` (neither debit nor credit) — financing is a separate arrangement
  between the customer and the finance company, outside StoreOS's customer-credit tracking.
  Only the actual down payment is recorded (as a `CustomerPayment` row on the sale, but NOT
  mirrored into the ledger either, per the corrected reasoning in the test description).
- `FinanceComposer` performs no DB access — pure composition over `FinanceEngine`, safe to unit
  test without fixtures (though this prompt's tests are integration-level since they go through
  the full checkout handler; a supplementary pure unit test of `FinanceComposer` alone is also
  worth adding to `StoreOS.Domain.Tests`... note `FinanceComposer` actually lives in
  `StoreOS.Application`, not `StoreOS.Domain`, since it composes `CheckoutFinanceDetails` — an
  Application-layer DTO; keep its test in `StoreOS.IntegrationTests` alongside the others,
  no DB needed for that specific narrow test but the project reference is already there).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter FinanceCheckoutTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-515 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-515 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-515` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `FinanceComposer`/`ComposedFinance` shapes, `Sale.RecordFinanceDetails`
signature, and the corrected rule that financed sales NEVER touch `customer_ledger_entries`
(neither debit nor credit) regardless of down payment or remainder.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-515: <one-line summary>"` only after verification is
   fully green.
