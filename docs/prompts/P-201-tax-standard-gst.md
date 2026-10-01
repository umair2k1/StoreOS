# P-201 — TaxEngine: StandardGst

**Phase:** 2 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-201** of the StoreOS Cloud build ("TaxEngine: StandardGst") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 1 backbone (P-101–119) complete. `Money.Round2` exists in
  `StoreOS.Domain.Common`.
- No tax engine exists yet. `StoreOS.Domain` remains 100% dependency-free (no EF, no MediatR
  references) — this and all Phase 2 prompts must preserve that.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Domain/Tax/LineTax.cs`:

```csharp
namespace StoreOS.Domain.Tax;

public readonly record struct LineTax(decimal Taxable, decimal Cgst, decimal Sgst)
{
    public decimal TotalGst => Cgst + Sgst;
    public static LineTax Zero => new(0m, 0m, 0m);
}
```

Create `backend/src/StoreOS.Domain/Tax/TaxEngine.cs`:

```csharp
using StoreOS.Domain.Common;

namespace StoreOS.Domain.Tax;

public static class TaxEngine
{
    public static LineTax StandardGst(decimal lineTotal, decimal gstRate)
    {
        if (lineTotal < 0) throw new ArgumentOutOfRangeException(nameof(lineTotal), "Line total cannot be negative.");
        if (gstRate < 0) throw new ArgumentOutOfRangeException(nameof(gstRate), "GST rate cannot be negative.");
        var taxable = Money.Round2(lineTotal / (1 + gstRate / 100m));
        var totalGst = Money.Round2(lineTotal - taxable);
        var cgst = Money.Round2(totalGst / 2m);
        var sgst = Money.Round2(totalGst - cgst);
        return new LineTax(taxable, cgst, sgst);
    }
}
```

Note `sgst = totalGst - cgst` (not `totalGst / 2m` again) — this guarantees `cgst + sgst`
always exactly equals `totalGst` even when `totalGst` has an odd number of paise, avoiding a
one-paisa reconciliation drift. This exact halves-reconcile pattern (Plan §7.1) must be
preserved everywhere GST is split into CGST/SGST throughout the codebase, including the
margin scheme engine in P-202.

Exact required test cases (compute these precisely; do not approximate):

1. `StandardGst(1180m, 18m)` -> taxable=1000.00, cgst=90.00, sgst=90.00 (totalGst=180.00)
2. `StandardGst(1000m, 0m)` -> taxable=1000.00, cgst=0.00, sgst=0.00 (zero GST rate)
3. `StandardGst(1250m, 12m)` -> taxable = 1250/1.12 = 1116.071428... -> Round2 = 1116.07;
   totalGst = 1250-1116.07 = 133.93; cgst = Round2(133.93/2) = 66.97 (66.965 rounds to 66.97
   half-up-away-from-zero); sgst = 133.93-66.97 = 66.96. (Compute and verify this exact
   result yourself with actual decimal arithmetic — do not trust this prose, run the numbers.)
4. `StandardGst(0m, 18m)` -> all zero.
5. `StandardGst(999.99m, 18m)` -> taxable = Round2(999.99/1.18) = Round2(847.4491525...) =
   847.45; totalGst = 999.99-847.45 = 152.54; cgst=76.27; sgst=76.27.
6. Multi-line reconciliation: given three lines with rates 18%, 12%, 0%, assert
   `Σ(taxable) + Σ(cgst) + Σ(sgst) == Σ(lineTotal)` exactly (no rounding drift across lines,
   since each line rounds independently — this is expected and correct per the spec, not a
   bug: assert per-LINE totals reconcile, not that summed pre-rounding values would match a
   differently-computed aggregate).
7. Negative `lineTotal` or negative `gstRate` throws `ArgumentOutOfRangeException`.

## 4. TASK

1. Create `LineTax.cs` and `TaxEngine.cs` (with only the `StandardGst` method for now
   — `MarginSchemeGst` is added in P-202) exactly as shown, in `StoreOS.Domain/Tax/`.
2. Write `backend/tests/StoreOS.Domain.Tests/Tax/StandardGstTests.cs` implementing EXACTLY
   the 7 numbered cases from the CONTEXT BLOCK, computing precise expected decimal values
   yourself (show your arithmetic in a code comment above each assertion so a reviewer can
   verify your math), using `FluentAssertions`'s `.Should().Be(...)` for decimal equality.
3. Confirm `StoreOS.Domain.csproj` has zero `PackageReference` entries beyond the banned-API
   analyzer from P-118 (`dotnet list package` on that project must show no EF/MediatR/HTTP
   dependencies).

## 5. HARD CONSTRAINTS

- `sgst` must always be computed as `totalGst - cgst`, never as an
  independent `Round2(totalGst / 2m)` — this is the load-bearing trick that guarantees
  `cgst + sgst == totalGst` exactly, with no drift. Every later engine that splits GST into
  CGST/SGST (P-202 margin scheme) must follow this identical pattern.
- All 7 test cases must use hand-computed exact expected values — do not write a test that
  merely asserts internal consistency (e.g. `cgst + sgst == totalGst`) as a SUBSTITUTE for
  concrete numeric assertions; you need both.

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter StandardGstTests
# Expected: 7 passed (or more, if you numbered sub-cases within the 7 items), 0 failed.
dotnet list backend/src/StoreOS.Domain/StoreOS.Domain.csproj package
# Expected: no EF Core / MediatR / ASP.NET package references.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-201 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-201 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-201` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `LineTax` record and `TaxEngine.StandardGst` method signatures, and the
"sgst = totalGst - cgst" reconciliation rule (referenced by every later GST-related prompt).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-201: <one-line summary>"` only after verification is
   fully green.
