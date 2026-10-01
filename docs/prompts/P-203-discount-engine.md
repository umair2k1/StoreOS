# P-203 — TaxEngine: proportional discount factor

**Phase:** 2 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-203** of the StoreOS Cloud build ("TaxEngine: proportional discount factor") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-201, P-202 complete: `TaxEngine.StandardGst` and `MarginSchemeGst` exist
  and are fully tested.
- No discount-application logic exists yet.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Domain/Tax/DiscountEngine.cs` (Plan §7.3):

```csharp
using StoreOS.Domain.Common;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Tax;

public static class DiscountEngine
{
    public static LineTax ApplyProportionalDiscount(LineTax before, decimal subtotal, decimal discount)
    {
        if (subtotal < 0) throw new ArgumentOutOfRangeException(nameof(subtotal));
        if (discount < 0)
            throw new DomainValidationException("discount_negative", "Discount cannot be negative.");
        if (discount > subtotal)
            throw new DomainValidationException("discount_exceeds_subtotal", "Discount cannot exceed subtotal.");
        if (subtotal == 0m)
        {
            // Nothing to discount proportionally against; factor is undefined -> treat as 1
            // (no-op) rather than dividing by zero, since a zero subtotal implies a zero
            // grand total regardless.
            return before;
        }
        var grand = subtotal - discount;
        var factor = grand / subtotal;
        var taxable = Money.Round2(before.Taxable * factor);
        var cgst = Money.Round2(before.Cgst * factor);
        var sgst = Money.Round2(before.Sgst * factor);
        return new LineTax(taxable, cgst, sgst);
    }
}
```

Exact required test cases:

1. `ApplyProportionalDiscount(LineTax(1000,90,90), subtotal:1180, discount:118)`: grand=1062;
   factor=1062/1180=0.9; taxable=Round2(1000*0.9)=900.00; cgst=Round2(90*0.9)=81.00;
   sgst=81.00.
2. `discount == 0` -> factor=1, result identical to `before` (verify field-by-field equality).
3. `discount == subtotal` (100% discount) -> factor=0 -> all fields become 0.00.
4. `discount > subtotal` -> throws `DomainValidationException` with
   `Code == "discount_exceeds_subtotal"`.
5. `discount < 0` -> throws `DomainValidationException` with `Code == "discount_negative"`.
6. `subtotal == 0` and `discount == 0` -> returns `before` unchanged (the zero-subtotal
   guard branch), does not throw a divide-by-zero.
7. Multi-line scenario: two lines with `before` values `(500,45,45)` and `(500,45,45)`
   (combined subtotal computed as sum of GROSS line totals, not taxable amounts — clarify in
   your test which "subtotal" means: it is the pre-discount GROSS invoice subtotal, i.e. the
   sum of `unitPrice*qty` across lines, NOT the sum of taxable bases; compute a concrete
   worked example with a stated gross subtotal and discount, apply the SAME factor to each
   line's `before` LineTax, and assert both lines scale identically).

## 4. TASK

1. Create `DiscountEngine.cs` exactly as shown in `StoreOS.Domain/Tax/`.
2. Write `backend/tests/StoreOS.Domain.Tests/Tax/DiscountEngineTests.cs` implementing all 7
   numbered cases with hand-computed exact values and exception type/code assertions where
   applicable.
3. Add a doc-comment on `ApplyProportionalDiscount` clarifying that `subtotal` here means the
   GROSS pre-discount invoice subtotal (sum of line totals), consistent with how
   `CheckoutHandler` (P-510) will call it — this comment prevents future ambiguity.

## 5. HARD CONSTRAINTS

- Discount validation order matters for correct error messages:
  check `discount < 0` OR `discount > subtotal` — both are independently possible failure
  modes with DISTINCT error codes (`discount_negative` vs `discount_exceeds_subtotal`); do
  not collapse them into one generic message.
- The `subtotal == 0` guard must come AFTER the negative/exceeds checks (a negative discount
  against a zero subtotal should still report `discount_negative`, not silently no-op) —
  order your `if` statements accordingly and add a test proving this ordering
  (`ApplyProportionalDiscount(LineTax.Zero, subtotal: 0, discount: -5)` must throw
  `discount_negative`, not return early).

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter DiscountEngineTests
# Expected: 7+ passed (including the zero-subtotal-with-negative-discount ordering case).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-203 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-203 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-203` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `DiscountEngine.ApplyProportionalDiscount` signature and the two distinct
error codes (`discount_negative`, `discount_exceeds_subtotal`).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-203: <one-line summary>"` only after verification is
   fully green.
