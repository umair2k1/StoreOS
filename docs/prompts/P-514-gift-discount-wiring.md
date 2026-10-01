# P-514 — Gift line handling + invoice-level discount wiring into checkout

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-514** of the StoreOS Cloud build ("Gift line handling + invoice-level discount wiring into checkout") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-510–513 complete: checkout works end-to-end with basic discount already
  partially wired (P-510's handler already applies `DiscountEngine.ApplyProportionalDiscount`
  at the AGGREGATE level). This prompt adds explicit, thorough TEST COVERAGE and refines gift
  handling which was only partially exercised so far (P-505's `PricingResolver` already zeroes
  gift-line tax; this prompt verifies the full checkout-level behavior and adds the missing
  reporting exclusion hook).

## 3. CONTEXT BLOCK

Review `docs/ARTIFACTS.md`'s P-510 record for the EXACT current
`CheckoutHandler.Handle` implementation before making any change.

Confirm/adjust: gift lines (`IsGift = true` on a `CheckoutCartLine`) still (a) validate and
consume stock/IMEI normally (a gift phone is still physically leaving the shelf), (b) are
recorded on `sale_items.is_gift = true`, (c) contribute `0` to `taxable`/`cgst`/`sgst` (already
handled by `PricingResolver`), and (d) contribute their FULL `price_per_unit * qty` to the
`subtotal` used for PROPORTIONAL DISCOUNT calculation or NOT? Resolve this ambiguity
explicitly: Requirements §6.8 says gift lines "must not be treated as normal sales revenue in
reports that explicitly exclude gifts" — this is a REPORTING concern (P-801's top-sellers
exclusion), not necessarily a checkout-math concern. For checkout's `GrandTotal` computation,
a gift line's `price_per_unit` should be included in `subtotal` for informational/display
purposes on the invoice (showing what was "given" at what nominal value) but since a gift's tax
is already zero, and if the gift is being included in `subtotal` at its nominal price, that
would incorrectly inflate `GrandTotal` (the customer isn't actually paying for the gift). Fix
this properly: gift lines contribute `0` to `subtotal`/`GrandTotal` (the customer pays nothing
for them) but the line itself still records the NOMINAL `price_per_unit` on `sale_items` for
informational display ("Free phone case worth ₹500" style receipt line) — separate "what's
displayed" from "what's charged." Update `CheckoutHandler`'s subtotal computation to EXCLUDE
gift lines from `subtotal`/`taxableBeforeDiscount`/etc:

```csharp
var chargeableLines = resolvedLines.Where(l => !l.IsGift).ToList();
var subtotal = chargeableLines.Sum(l => l.PricePerUnit * l.Qty);
var taxableBeforeDiscount = chargeableLines.Sum(l => l.LineTaxable);
var cgstBeforeDiscount = chargeableLines.Sum(l => l.LineCgst);
var sgstBeforeDiscount = chargeableLines.Sum(l => l.LineSgst);
```
(Gift lines are still iterated separately for stock/IMEI reservation and `SaleItem` creation —
only the FINANCIAL aggregation excludes them.)

Also add a `top-sellers exclusion` hook: since P-801 (dashboard) hasn't been built yet, just
confirm `sale_items.is_gift` is reliably persisted (already true per P-502's schema) so P-801
can filter on it later — no new code needed here beyond the subtotal fix, this prompt is
mostly about CORRECTING and THOROUGHLY TESTING the aggregation logic.

## 4. TASK

1. Apply the `chargeableLines`-excludes-gifts fix to `CheckoutHandler.Handle`'s
   subtotal/taxable/cgst/sgst aggregation, exactly as shown, WITHOUT changing how gift lines are
   handled for stock reservation or `SaleItem` persistence (those remain unchanged, gift lines
   still get their own `SaleItem` row with the nominal price recorded).
2. Write `StoreOS.IntegrationTests/Sales/GiftAndDiscountCheckoutTests.cs`: (a) a cart with one
   paid line (₹1000, 18% GST) and one gift line (nominal ₹500) -> `GrandTotal` reflects ONLY
   the paid line's ₹1000 (minus any discount), the gift line's `SaleItem` row still shows
   `price_per_unit=500`, `is_gift=true`, `taxable=0`, `cgst=0`, `sgst=0`; stock is decremented
   for BOTH lines; (b) an invoice-level discount applied to a cart with both a paid and a gift
   line -> the discount factor is computed against the PAID-ONLY subtotal, not inflated by the
   gift's nominal value (verify the exact numeric result); (c) a cart of ONLY gift lines (a
   pure giveaway, no paid items) -> `GrandTotal = 0`, `AmountPaid` must also be 0 (a payment
   list with a nonzero amount against a zero-total sale should be REJECTED — add this specific
   guard to `CheckoutHandler`: if `grandTotal == 0` and any payment amount > 0, throw
   `DomainValidationException("payment_exceeds_balance", ...)`, reusing `Sale.ApplyPayment`'s
   existing cap logic which should already catch this via the entity guard from P-502; confirm
   it does, or add the explicit check if not).

## 5. HARD CONSTRAINTS

- Gift lines NEVER contribute to `subtotal`/`GrandTotal`/taxable
  amounts — they are recorded for informational/audit purposes on the invoice only, at their
  nominal price, with zero actual charge and zero tax.
- Discount is always computed against the PAID (non-gift) subtotal only.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter GiftAndDiscountCheckoutTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-514 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-514 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-514` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the corrected `chargeableLines`-based aggregation logic in
`CheckoutHandler`, confirming gifts are excluded from all financial totals but still recorded
and still consume stock.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-514: <one-line summary>"` only after verification is
   fully green.
