# P-206 — Invoice number formatter (both series formats, pure)

**Phase:** 2 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-206** of the StoreOS Cloud build ("Invoice number formatter (both series formats, pure)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-201–205 complete: full domain engine test suite green.
- No invoice-number formatting logic exists yet. This is the PURE formatting piece only —
  the actual sequence ALLOCATION (concurrency-safe upsert into `invoice_sequences`) is a
  separate concern built in P-501; this prompt only turns `(branchCode, financialYear or
  date, sequenceNumber)` into the final formatted string and validates the ≤16-char Rule
  46(b) constraint.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** add `FormatEstimate(string branchCode, string fyLabel, int seq)` → `$"{branchCode}/E/{fyLabel}/{seq:D6}"` with tests (`B01/E/26-27/000007`). Estimates use their own series (DECISIONS D-07) and are exempt from the 16-character check. The 16-character check for tax invoices stays. The DAILY timestamp format (`B01-20260923-143005-007`, 23 chars) is NOT Rule 46(b)/e-invoice compliant: keep it only as an explicit legacy option, log a warning when a tenant selects it, and mention it in the CA sign-off checklist (DECISIONS O-01).

Create `backend/src/StoreOS.Domain/Sales/InvoiceSeriesFormat.cs`:

```csharp
namespace StoreOS.Domain.Sales;

public enum InvoiceSeriesFormat
{
    FyConsecutive,
    DailyTimestamp
}
```

Create `backend/src/StoreOS.Domain/Sales/InvoiceNumberFormatter.cs` (Plan §8.5):

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Sales;

public static class InvoiceNumberFormatter
{
    public static string FormatFyConsecutive(string branchCode, string financialYearLabel, int sequence)
    {
        // e.g. FormatFyConsecutive("B01", "26-27", 42) -> "B01/26-27/000042"
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code is required.", nameof(branchCode));
        if (sequence <= 0)
            throw new DomainValidationException("invoice_sequence_invalid", "Sequence must be a positive integer.");
        var formatted = $"{branchCode}/{financialYearLabel}/{sequence:D6}";
        if (formatted.Length > 16)
            throw new DomainValidationException("invoice_number_exceeds_rule46b",
                $"Invoice number '{formatted}' exceeds the 16-character limit mandated by CGST Rule 46(b).");
        return formatted;
    }

    public static string FormatDailyTimestamp(string branchCode, DateTimeOffset businessDateTime, int dailySequence)
    {
        // e.g. FormatDailyTimestamp("B01", 2026-09-23T14:30:05, 7) -> "B01-20260923-143005-007"
        if (string.IsNullOrWhiteSpace(branchCode))
            throw new ArgumentException("Branch code is required.", nameof(branchCode));
        if (dailySequence <= 0)
            throw new DomainValidationException("invoice_sequence_invalid", "Sequence must be a positive integer.");
        var formatted = $"{branchCode}-{businessDateTime:yyyyMMdd-HHmmss}-{dailySequence:D3}";
        return formatted;
        // Note: this format is NOT subject to the same 16-char cap check because Rule 46(b)
        // treats it as a "legacy/daily" series acknowledged to carry audit risk (see
        // Requirements §8.2's CA Confirmation Requirement) — it is intentionally longer and
        // permitted only via explicit tenant configuration + CA signoff, never the default.
    }

    public static string FinancialYearLabel(DateOnly businessDate)
    {
        // Indian financial year runs Apr 1 - Mar 31. E.g. 2026-09-23 -> "26-27";
        // 2027-02-10 -> "26-27"; 2027-04-01 -> "27-28".
        int fyStartYear = businessDate.Month >= 4 ? businessDate.Year : businessDate.Year - 1;
        int fyEndYearShort = (fyStartYear + 1) % 100;
        int fyStartYearShort = fyStartYear % 100;
        return $"{fyStartYearShort:D2}-{fyEndYearShort:D2}";
    }
}
```

Exact required test cases:

1. `FormatFyConsecutive("B01", "26-27", 42)` -> `"B01/26-27/000042"` (exactly 17 characters —
   WAIT: count precisely: "B01/26-27/000042" = B,0,1,/,2,6,-,2,7,/,0,0,0,0,4,2 = 16 characters
   exactly. Verify this character count yourself precisely before asserting; if your
   implementation produces 17 characters for a 3-char branch code, that is a genuine finding —
   report it and adjust the padding width (`D6` -> fewer digits, or require branch codes ≤3
   chars) so the DEFAULT example from Requirements §8.2 (`B01/26-27/000042`) fits in exactly
   16 characters. Confirm the exact length with `"B01/26-27/000042".Length` conceptually before
   writing the test assertion; write the test to assert `.Length <= 16` AND the exact string
   value, not just the length.
2. `FormatFyConsecutive("B01", "26-27", 0)` -> throws `invoice_sequence_invalid`.
3. `FormatFyConsecutive("B01", "26-27", -5)` -> throws `invoice_sequence_invalid`.
4. A branch code long enough to push the formatted string over 16 chars (e.g. branch code
   `"BRANCH01"` with FY `"26-27"` and sequence `123456` -> `"BRANCH01/26-27/123456"`, count
   its length; if over 16, assert it throws `invoice_number_exceeds_rule46b`).
5. `FormatDailyTimestamp("B01", new DateTimeOffset(2026,9,23,14,30,5,TimeSpan.Zero), 7)` ->
   `"B01-20260923-143005-007"`.
6. `FinancialYearLabel(new DateOnly(2026,9,23))` -> `"26-27"`.
7. `FinancialYearLabel(new DateOnly(2027,2,10))` -> `"26-27"` (still within FY 2026-27, since
   Indian FY runs Apr-Mar).
8. `FinancialYearLabel(new DateOnly(2027,4,1))` -> `"27-28"` (FY rolls over exactly on Apr 1).

## 4. TASK

1. Create `InvoiceSeriesFormat.cs` and `InvoiceNumberFormatter.cs` exactly as shown
   (adjusting the `D6`/format width if your own precise character-count check in test case 1
   reveals a discrepancy — report any such adjustment explicitly).
2. Write `backend/tests/StoreOS.Domain.Tests/Sales/InvoiceNumberFormatterTests.cs`
   implementing all 8 numbered cases, computing exact string lengths and values yourself
   (do not guess — count characters explicitly, e.g. via `.Length` assertions alongside exact
   string equality checks).

## 5. HARD CONSTRAINTS

- The FY-consecutive format's 16-character cap check
  (`invoice_number_exceeds_rule46b`) must use the ACTUAL formatted string's `.Length`, not an
  estimate — compute it at runtime, never hardcode an assumption about branch code length.
- The daily-timestamp format has no 16-char cap enforced in code (per the Requirements §8.2
  CA-confirmation caveat) — do not add one; this asymmetry between the two formats is
  intentional and documented.

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter InvoiceNumberFormatterTests
# Expected: 8 passed, with exact string/length assertions for both formats and the FY-label
# rollover logic.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-206 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-206 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-206` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `InvoiceNumberFormatter` method signatures, the confirmed exact character
count of the reference example `B01/26-27/000042` (should be 16), and the
`FinancialYearLabel` Apr-1 rollover rule.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-206: <one-line summary>"` only after verification is
   fully green.
