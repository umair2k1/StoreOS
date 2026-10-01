# P-118 — Money/decimal conventions + Money.Round2 utility + global analyzer ban on double/float in Domain

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-118** of the StoreOS Cloud build ("Money/decimal conventions + Money.Round2 utility + global analyzer ban on double/float in Domain") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Backbone prompts P-101–117 are complete. No monetary calculation utility
  exists yet. This is a small, self-contained prompt but it is a HARD DEPENDENCY for every
  domain engine in Phase 2 (P-201–206) — do it before Phase 2 starts.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Domain/Common/Money.cs`:

```csharp
namespace StoreOS.Domain.Common;

public static class Money
{
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
```

Add a Roslyn banned-API analyzer to PREVENT `double`/`float` from ever being used for monetary
math inside `StoreOS.Domain`. Create `backend/src/StoreOS.Domain/BannedSymbols.txt`:

```text
M:System.Math.Pow(System.Double,System.Double);Use decimal-based DecimalPow instead of Math.Pow for financial calculations (see FinanceEngine).
T:System.Double;Do not use double for monetary values; use decimal.
T:System.Single;Do not use float for monetary values; use decimal.
```

Add the `Microsoft.CodeAnalysis.BannedApiAnalyzers` NuGet package (NOT in the P-004 allowlist
yet — add it now as an addition to `Directory.Packages.props`, version `3.11.0`, restricted to
`StoreOS.Domain.csproj` only, as an analyzer-only reference):

```xml
<PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.11.0" />
```

In `StoreOS.Domain.csproj`:
```xml
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />
  <AdditionalFiles Include="BannedSymbols.txt" />
</ItemGroup>
```

## 4. TASK

1. Update `backend/Directory.Packages.props` to add the
   `Microsoft.CodeAnalysis.BannedApiAnalyzers` version `3.11.0` entry (this is a deliberate,
   documented, one-time addition to the P-004 allowlist — record it in `docs/ARTIFACTS.md`
   under a "Package allowlist amendments" heading so the addition is traceable).
2. Create `backend/src/StoreOS.Domain/Common/Money.cs` exactly as shown.
3. Create `backend/src/StoreOS.Domain/BannedSymbols.txt` exactly as shown and wire it via
   `<AdditionalFiles>` plus the analyzer `PackageReference` in `StoreOS.Domain.csproj` only
   (NOT in `StoreOS.Application`/`StoreOS.Infrastructure`/`StoreOS.Api` — money math purity is
   specifically a `StoreOS.Domain` concern; other layers may need `double` for unrelated
   things like non-monetary telemetry sampling rates, so do not ban it project-wide).
4. Write `StoreOS.Domain.Tests/Common/MoneyTests.cs` with these EXACT numeric cases (compute
   them yourself and assert precisely):
   - `Money.Round2(10.005m)` -> `10.01m` (half-up, not banker's rounding)
   - `Money.Round2(10.004m)` -> `10.00m`
   - `Money.Round2(10.015m)` -> `10.02m`
   - `Money.Round2(-10.005m)` -> `-10.01m` (away from zero applies to negatives too)
   - `Money.Round2(0m)` -> `0.00m` (compare as decimal equality, not string)
5. Confirm the banned-API analyzer actually fires: temporarily add a throwaway
   `double x = 1.0;` line to a scratch file inside `StoreOS.Domain`, run `dotnet build`, and
   confirm a `RS0030` (banned symbol) diagnostic appears; then remove the throwaway line.
   Document that you performed this check in `docs/BUILD_LOG.md`.

## 5. HARD CONSTRAINTS

- `Money.Round2` is the ONLY sanctioned rounding function for
  monetary values anywhere in the codebase — every later prompt performing money arithmetic
  must call this, never `Math.Round` directly, never `decimal.Round`, so behavior stays
  centralized and consistent.
- The banned-symbols analyzer applies ONLY to `StoreOS.Domain` — do not extend it to other
  projects in this prompt (Infrastructure may legitimately need `double` for e.g. HTTP client
  timeout jitter calculations in the Polly retry policy of P-511).

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter MoneyTests
# Expected: 5 passed, all half-up-away-from-zero cases correct.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-118 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-118 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-118` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact `Money.Round2` signature (`StoreOS.Domain.Common.Money`), and
confirmation that the RS0030 banned-API analyzer is active and scoped to `StoreOS.Domain`
only.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-118: <one-line summary>"` only after verification is
   fully green.
