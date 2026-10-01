# P-204 — FinanceEngine: EMI with DecimalPow

**Phase:** 2 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-204** of the StoreOS Cloud build ("FinanceEngine: EMI with DecimalPow") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-201–203 complete: full tax/discount engine tested.
- No EMI/finance calculation exists yet.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Domain/Finance/FinanceEngine.cs` (Plan §7.4,
verbatim reference implementation):

```csharp
using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Finance;

public static class FinanceEngine
{
    public static decimal CalculatePrincipal(decimal grandTotal, decimal downPayment)
    {
        if (downPayment < 0)
            throw new DomainValidationException("finance_down_payment_negative", "Down payment cannot be negative.");
        if (downPayment > grandTotal)
            throw new DomainValidationException("finance_down_payment_exceeds_total", "Down payment cannot exceed grand total.");
        var principal = grandTotal - downPayment;
        if (principal <= 0)
            throw new DomainValidationException("finance_principal_not_positive", "Financed principal must be greater than zero.");
        return principal;
    }

    public static decimal CalculateEmi(decimal principal, decimal annualRatePercent, int months)
    {
        if (principal <= 0)
            throw new DomainValidationException("finance_principal_not_positive", "Financed principal must be greater than zero.");
        if (months <= 0)
            throw new DomainValidationException("finance_invalid_tenure", "Tenure in months must be greater than zero.");
        if (annualRatePercent < 0)
            throw new DomainValidationException("finance_invalid_rate", "Annual interest rate cannot be negative.");

        if (annualRatePercent == 0m)
            return Money.Round2(principal / months);

        var r = annualRatePercent / 12m / 100m;
        var pow = DecimalPow(1m + r, months);
        var emi = principal * r * pow / (pow - 1m);
        return Money.Round2(emi);
    }

    private static decimal DecimalPow(decimal baseVal, int exp)
    {
        decimal result = 1m;
        for (int i = 0; i < exp; i++)
        {
            result *= baseVal;
        }
        return result;
    }
}
```

Exact required test cases:

1. **Zero interest:** `CalculateEmi(principal: 12000m, annualRatePercent: 0m, months: 12)` ->
   `Money.Round2(12000/12)` = `1000.00`.
2. **Positive interest:** `CalculateEmi(principal: 12000m, annualRatePercent: 12m, months:
   12)`: r = 12/12/100 = 0.01; pow = 1.01^12 (compute via repeated multiplication exactly as
   the loop does — you must compute this precisely with actual decimal arithmetic, e.g. using
   a calculator or by reasoning through the loop; report the exact decimal value of
   `1.01^12` to at least 10 significant digits in a comment, then compute
   `emi = 12000 * 0.01 * pow / (pow - 1)` and round to 2dp). Do the arithmetic precisely — do
   not guess.
3. **Invalid tenure:** `CalculateEmi(principal: 12000m, annualRatePercent: 10m, months: 0)`
   throws `DomainValidationException` code `finance_invalid_tenure`. Also test `months: -3`.
4. **Zero principal:** `CalculateEmi(principal: 0m, annualRatePercent: 10m, months: 12)`
   throws code `finance_principal_not_positive`.
5. **Negative rate:** `CalculateEmi(principal: 10000m, annualRatePercent: -5m, months: 12)`
   throws code `finance_invalid_rate`.
6. **Down payment greater than total:** `CalculatePrincipal(grandTotal: 10000m, downPayment:
   15000m)` throws code `finance_down_payment_exceeds_total`.
7. **Down payment equals total exactly:** `CalculatePrincipal(grandTotal: 10000m,
   downPayment: 10000m)` -> principal would be 0 -> throws `finance_principal_not_positive`
   (financing a zero principal makes no sense — the sale should just be a full cash payment,
   not a "0-EMI" finance record).
8. **Negative down payment:** `CalculatePrincipal(grandTotal: 10000m, downPayment: -100m)`
   throws code `finance_down_payment_negative`.
9. Confirm `DecimalPow` never calls `Math.Pow` — grep the file to prove it (this is also
   enforced by the P-118 banned-API analyzer, but write an explicit test-time grep assertion
   too as a belt-and-suspenders check inside the test file's static constructor or a dedicated
   `[Fact] Source_never_calls_MathPow()` test that reads its own source file... simplify: just
   confirm via code review comment that `Math.Pow` does not appear; do not over-engineer a
   meta-test for this, rely on the P-118 analyzer, but DO still keep this as an explicit item
   you self-check before finishing.

## 4. TASK

1. Create `FinanceEngine.cs` exactly as shown in `StoreOS.Domain/Finance/`.
2. Write `backend/tests/StoreOS.Domain.Tests/Finance/FinanceEngineTests.cs` implementing all 8
   numbered numeric/exception cases (case 9 is a self-check, not a separate xunit fact) with
   hand-computed exact values (show the `1.01^12` computation explicitly in a comment for
   case 2) and precise exception code assertions for cases 3, 4, 5, 6, 7, 8.
3. Confirm `dotnet build` produces the RS0030 banned-symbol diagnostic if you temporarily
   insert a `Math.Pow` call anywhere in this file (repeat the P-118 self-check specifically
   for this new file), then remove it.

## 5. HARD CONSTRAINTS

- `DecimalPow` must remain a plain integer-exponent loop — never
  replace it with `Math.Pow` cast back to decimal, even "just for large `months` values" —
  this is a permanent, non-negotiable invariant per Plan §7.4's note on IEEE 754 precision
  loss.
- `CalculatePrincipal` and `CalculateEmi` are separate functions with separate validation —
  `CalculateEmi` does NOT re-derive principal from grandTotal/downPayment; the caller
  (P-515's `FinanceComposer`) calls `CalculatePrincipal` first, then passes its result into
  `CalculateEmi`. Keep them decoupled as shown.

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter FinanceEngineTests
# Expected: 8 passed (zero-interest, positive-interest, invalid tenure incl. negative,
# zero principal, negative rate, down-payment-exceeds-total, down-payment-equals-total,
# negative down payment).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-204 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-204 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-204` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `FinanceEngine.CalculatePrincipal` and `CalculateEmi` signatures, and all
five distinct error codes (`finance_down_payment_negative`,
`finance_down_payment_exceeds_total`, `finance_principal_not_positive`,
`finance_invalid_tenure`, `finance_invalid_rate`).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-204: <one-line summary>"` only after verification is
   fully green.
