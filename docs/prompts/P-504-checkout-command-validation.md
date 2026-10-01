# P-504 — CheckoutCommand + validator (cart shape, qty>0, payment shape) — validation-only

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-504** of the StoreOS Cloud build ("CheckoutCommand + validator (cart shape, qty>0, payment shape) — validation-only") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-503 complete: POS search/browse works.
- This prompt defines the FULL shape of the checkout request and its structural validation
  ONLY — no persistence, no pricing, no stock mutation yet (those come in P-505/P-510). This
  keeps the eventual `CheckoutHandler` prompt (P-510) focused purely on orchestration logic
  rather than re-deriving the command shape from scratch.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Sales/Checkout/CheckoutCommand.cs`
— this is the FULL, FINAL shape of the checkout request, covering every feature from
Requirements §6-10 (cart lines, customer, payment, gifts, discount, finance, margin scheme,
IMEI). Later prompts (P-505 onward) will consume this exact shape without modifying it further
except where explicitly noted:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.Checkout;

public record CheckoutCartLine(Guid ProductId, int Qty, string? Imei, bool UseMarginScheme,
    bool IsGift, decimal? OverridePricePerUnit);

public record CheckoutPayment(string PaymentMode, decimal Amount);

public record CheckoutFinanceDetails(string FinanceProvider, string? FinancerName,
    decimal DownPayment, int TenureMonths, decimal AnnualInterestRate);

public record CheckoutCommand(
    IReadOnlyList<CheckoutCartLine> Lines,
    Guid? CustomerId,
    string? WalkInCustomerName,
    decimal InvoiceDiscount,
    string InvoiceType, // "TaxInvoice" or "Estimate"
    IReadOnlyList<CheckoutPayment> Payments,
    CheckoutFinanceDetails? Finance,
    string? Description
) : IRequest<CheckoutResult>, IRequiresPermission
{
    public string RequiredPermission => "pos:checkout";
}

public record CheckoutResult(Guid SaleId, string InvoiceNumber, decimal GrandTotal,
    decimal AmountPaid, decimal Balance, DateTimeOffset SaleDate);
```

Create `backend/src/StoreOS.Application/Sales/Checkout/CheckoutValidator.cs`:

```csharp
using FluentValidation;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.Checkout;

public class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Cart cannot be empty.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Qty).GreaterThan(0);
            line.RuleFor(l => l.OverridePricePerUnit).GreaterThanOrEqualTo(0)
                .When(l => l.OverridePricePerUnit.HasValue);
        });
        RuleFor(x => x.InvoiceDiscount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.InvoiceType).Must(t => InvoiceType.All.Contains(t))
            .WithMessage("Invoice type must be TaxInvoice or Estimate.");
        RuleFor(x => x.Payments).NotNull();
        RuleForEach(x => x.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.Amount).GreaterThan(0);
            p.RuleFor(x => x.PaymentMode).Must(m => PaymentMode.All.Contains(m));
        });
        RuleFor(x => x.Finance).SetValidator(new CheckoutFinanceDetailsValidator()!)
            .When(x => x.Finance is not null);
        RuleFor(x => x)
            .Must(x => x.CustomerId is not null || !string.IsNullOrWhiteSpace(x.WalkInCustomerName) || true)
            .WithMessage("A walk-in name is recommended when no customer is selected.");
            // Note: per Requirements §6.6, "Walk-in Customer" is an acceptable DEFAULT even
            // with no name provided, so this rule is deliberately permissive (always true) —
            // the handler (P-510) applies the literal default string "Walk-in Customer" when
            // both CustomerId and WalkInCustomerName are absent. Keep this rule as
            // documentation of intent rather than an actual constraint.
    }
}

public class CheckoutFinanceDetailsValidator : AbstractValidator<CheckoutFinanceDetails>
{
    public CheckoutFinanceDetailsValidator()
    {
        RuleFor(x => x.FinanceProvider).NotEmpty();
        RuleFor(x => x.DownPayment).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TenureMonths).GreaterThan(0);
        RuleFor(x => x.AnnualInterestRate).GreaterThanOrEqualTo(0);
    }
}
```

This prompt does NOT create a handler yet — write a MINIMAL placeholder-free handler that
performs ONLY structural echo-back for testing purposes... actually per the anti-stub rule, we
cannot write a "placeholder" handler. Resolve this: do NOT register a handler for
`CheckoutCommand` in this prompt at all — MediatR will simply have no handler registered yet,
which is fine since NOTHING calls `IMediator.Send(new CheckoutCommand(...))` until P-510 wires
the controller action. Test ONLY the validator directly (instantiate `CheckoutValidator` and
call `.Validate(...)` in a unit test), never through the full MediatR pipeline, since no
handler exists yet.

## 4. TASK

1. Create `CheckoutCommand.cs` (with all nested records: `CheckoutCartLine`,
   `CheckoutPayment`, `CheckoutFinanceDetails`, `CheckoutResult`) and `CheckoutValidator.cs`
   (with `CheckoutFinanceDetailsValidator`) exactly as shown, in
   `StoreOS.Application/Sales/Checkout/`.
2. Do NOT create a handler or register anything with MediatR yet — that begins in P-505/P-510.
3. Write `StoreOS.Application.Tests`... note: no such test project exists per P-001's solution
   layout (only `StoreOS.Domain.Tests`, `StoreOS.IntegrationTests`, `StoreOS.ConcurrencyTests`)
   — validator-only unit tests belong in `StoreOS.IntegrationTests` since that project already
   references `StoreOS.Application`; do NOT create a new test project just for this. Write
   `StoreOS.IntegrationTests/Sales/CheckoutValidatorTests.cs` (plain xunit facts instantiating
   `new CheckoutValidator()` directly, no `WebApplicationFactory`/HTTP involved): (a) empty
   `Lines` -> validation failure; (b) a line with `Qty = 0` -> failure; (c) negative
   `InvoiceDiscount` -> failure; (d) `InvoiceType = "Bogus"` -> failure; (e) a payment with
   `PaymentMode = "Bitcoin"` -> failure; (f) `Finance` present with `TenureMonths = 0` ->
   failure (nested validator fires); (g) a fully well-formed command -> passes with zero errors.

## 5. HARD CONSTRAINTS

- Do NOT register a MediatR handler for `CheckoutCommand` in this
  prompt — that is explicitly deferred to later prompts (pricing in P-505, full assembly in
  P-510) so each piece can be built and tested incrementally without a half-finished handler
  sitting in the codebase (which would itself risk violating the anti-stub rule if written
  incompletely).
- This `CheckoutCommand` shape is FINAL — later prompts (P-505, P-509, P-510, P-514, P-515,
  P-517) build handler logic that CONSUMES this exact shape; they do not redefine or rename its
  fields. If a later prompt's needs genuinely require a new field, that is a manifest
  inconsistency to flag explicitly, not something to fix silently.

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
dotnet build StoreOS.sln
dotnet test backend/tests/StoreOS.IntegrationTests --filter CheckoutValidatorTests
# Expected: 7 passed, 0 failed. No MediatR handler exists yet for CheckoutCommand — this is
# expected and correct at this point in the build.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-504 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-504 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-504` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the COMPLETE, FINAL `CheckoutCommand` shape (all nested records verbatim) —
this is the single most important artifact record in the entire manifest, since every
checkout-related prompt from here through P-525 depends on quoting it exactly.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-504: <one-line summary>"` only after verification is
   fully green.
