# P-202 — TaxEngine: MarginSchemeGst + cost-guard

**Phase:** 2 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-202** of the StoreOS Cloud build ("TaxEngine: MarginSchemeGst + cost-guard") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-201 complete: `TaxEngine.StandardGst` exists and is fully tested;
  `LineTax` record exists.
- `DomainException` / `DomainValidationException` already live in `StoreOS.Domain.Exceptions` (created there by P-113 in this revised build). Do NOT move or duplicate them; just use them.

## 3. CONTEXT BLOCK

(The exception types are exactly those created in P-113; nothing to create here.)

Extend `backend/src/StoreOS.Domain/Tax/TaxEngine.cs` with `MarginSchemeGst` (Plan §7.2,
verbatim reference implementation, namespace-adjusted):

```csharp
public static LineTax MarginSchemeGst(decimal sell, decimal cost, decimal gstRate)
{
    // Hard domain validation: cost basis MUST be strictly positive.
    // Silent fallback to cost = 0 is prohibited (would treat full selling price as profit
    // margin, miscalculating GST and creating severe statutory audit liability under CGST
    // Rule 32(5)).
    if (cost <= 0)
        throw new DomainValidationException("margin_scheme_invalid_cost_basis",
            "Margin scheme requires a known positive cost basis (> 0). Cost cannot be zero or empty.");

    var margin = sell - cost;
    if (margin <= 0) return LineTax.Zero;
    var taxable = Money.Round2(margin / (1 + gstRate / 100m));
    var totalGst = Money.Round2(margin - taxable);
    var cgst = Money.Round2(totalGst / 2m);
    return new LineTax(taxable, cgst, Money.Round2(totalGst - cgst));
}
```

Exact required test cases:

1. `MarginSchemeGst(sell: 15000m, cost: 12000m, gstRate: 18m)`: margin=3000; taxable =
   Round2(3000/1.18) = Round2(2542.372881...) = 2542.37; totalGst = 3000-2542.37 = 457.63;
   cgst = Round2(457.63/2) = 228.82 (228.815 half-up-away-from-zero -> 228.82... verify: 228.815
   rounds to 228.82 since .815 rounds up at the second decimal boundary — compute precisely and
   confirm); sgst = 457.63-228.82 = 228.81.
2. `MarginSchemeGst(sell: 10000m, cost: 10000m, gstRate: 18m)` -> margin=0 -> `LineTax.Zero`
   (margin <= 0 branch).
3. `MarginSchemeGst(sell: 9000m, cost: 10000m, gstRate: 18m)` -> margin=-1000 (negative) ->
   `LineTax.Zero`.
4. `MarginSchemeGst(sell: 10000m, cost: 0m, gstRate: 18m)` -> throws
   `DomainValidationException` with `Code == "margin_scheme_invalid_cost_basis"`.
5. `MarginSchemeGst(sell: 10000m, cost: -500m, gstRate: 18m)` -> throws the same exception
   (negative cost also rejected, not just exactly zero).
6. `MarginSchemeGst(sell: 10000m, cost: 1m, gstRate: 0m)` -> gstRate 0 -> margin=9999,
   taxable=9999.00, cgst=0, sgst=0 (zero-rate margin sale still computes margin correctly, tax
   is just zero).

## 4. TASK

1. Verify `DomainException`/`DomainValidationException` exist in `StoreOS.Domain.Exceptions` (P-113); do not move anything. Update
   every existing reference (P-113's `ExceptionHandlingMiddleware`, P-114's
   `AuthorizationBehavior`) to use the new namespace via `using StoreOS.Domain.Exceptions;`.
   Confirm `dotnet build` still succeeds after the move.
2. Add `MarginSchemeGst` to `TaxEngine.cs` exactly as shown.
3. Write `backend/tests/StoreOS.Domain.Tests/Tax/MarginSchemeGstTests.cs` implementing all 6
   numbered cases with hand-computed exact decimal expectations (show your arithmetic in
   comments) and asserting the exception TYPE and `Code` property precisely for cases 4–5.
4. Re-run the full `StoreOS.IntegrationTests` suite (specifically
   `ExceptionHandlingTests` from P-113) to confirm the namespace move did not break the 422
   mapping for domain exceptions.

## 5. HARD CONSTRAINTS

- `cost <= 0` (using `<=`, not `< 0`) must reject BOTH exactly-zero
  and negative cost values — this is the hard validation guard; a bare "cost is missing"
  check is insufficient, the guard is purely numeric on the resolved decimal value.
- Margin scheme is a pure function here — it does NOT know or care whether the item is a
  "Used Phone" or which cost source (IMEI vs branch inventory vs product default) supplied
  `cost`. That business-rule cascade belongs in `PricingResolver` (P-505), which calls this
  function with an already-resolved `cost` value. Do not embed category checks here.

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter MarginSchemeGstTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter ExceptionHandlingTests
# Expected: all green; namespace move caused no regression.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-202 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-202 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-202` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the new namespace location of `DomainException`/`DomainValidationException`
(`StoreOS.Domain.Exceptions`), the `MarginSchemeGst` signature, and the exact error code
string `margin_scheme_invalid_cost_basis`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-202: <one-line summary>"` only after verification is
   fully green.
