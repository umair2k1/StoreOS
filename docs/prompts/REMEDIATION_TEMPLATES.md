# StoreOS Cloud — Remediation Prompt Templates

Use one of these five templates whenever P-1205's Completeness Audit (or any later manual
review) finds a gap. Each follows the same 9-section anatomy as every numbered prompt in this
manifest. Fill in every `<...>` slot precisely before handing the prompt to the executor. Do
not leave a slot unfilled — an unfilled slot is itself a form of under-specification the whole
manifest was built to avoid.

Pick the template whose shape most closely matches the discovered gap. If none fits, use
Template 5 (Generic Gap) and describe the gap in full, following the same discipline as every
other prompt in this manifest: paste exact code/schema/contracts, never say "see X."

---

## Template 1 — Missing or Incomplete Backend Behavior

```
# REMEDIATION — <short title>

**Phase:** Remediation &nbsp;|&nbsp; **Minimum executor reasoning level:** <low|med|high>

## 1. ROLE & MISSION
You are implementing a remediation fix in the StoreOS Cloud build in the Oh My Pi IDE. A gap
was found during the Completeness Audit (P-1205) or a later review: THE GAP: <describe the
exact missing/incorrect behavior, the file(s) involved, and which requirement or invariant it
violates — quote the relevant Requirements/Plan section number>.

## 2. WORLD STATE
- The full StoreOS Cloud backend/frontend (through prompt <ID>) is otherwise complete and green.
- The specific defect is: <exact defect, with a failing test name/error message if one exists>.
- Do not modify: <list any files that must stay untouched>.

## 3. CONTEXT BLOCK
<Paste the CURRENT, ACTUAL source of every file that needs to change, verbatim, read fresh from
the repository — never from memory or an earlier prompt's possibly-stale record. Paste the
exact signature of every dependency the fix will call.>

## 4. TASK
1. <Exact, numbered steps to close the gap, with exact file paths and method/class names.>
2. <...>

## 5. HARD CONSTRAINTS
<Global Invariants block, verbatim from MANIFEST.md's boilerplate, plus any gap-specific
constraint.>

## 6. FORBIDDEN PATTERNS
<Anti-Stub Clause, verbatim from MANIFEST.md's boilerplate.>

## 7. VERIFICATION
```bash
<exact commands, including the specific failing test(s) that must now pass, plus a full
regression run of the affected test project(s)>
```

## 8. FAILURE PROTOCOL
<Standard failure protocol, verbatim from MANIFEST.md's boilerplate, referencing this
remediation prompt's own ID (e.g. "REMEDIATION-001").>

## 9. REPORT
Append to docs/BUILD_LOG.md and docs/ARTIFACTS.md exactly as every other prompt does, noting
this was a remediation of a gap found in <P-1205 / later review date>.
```

---

## Template 2 — Missing or Incomplete Frontend Screen/Component

```
# REMEDIATION — <short title>

**Phase:** Remediation &nbsp;|&nbsp; **Minimum executor reasoning level:** <low|med|high>

## 1. ROLE & MISSION
You are implementing a remediation fix in the StoreOS Cloud build in the Oh My Pi IDE.
THE GAP: <describe the missing/incorrect UI behavior, the component(s) involved, and which
Requirements section or user flow it breaks>.

## 2. WORLD STATE
- The full Angular frontend (through prompt <ID>) is otherwise complete and green.
- The specific defect: <exact defect — e.g. a button with no handler, a missing permission
  gate, a filter that doesn't update the URL, a form accepting invalid input>.
- Do not modify: <list any files/components that must stay untouched>.

## 3. CONTEXT BLOCK
<Paste the CURRENT, ACTUAL component/service/template source verbatim. Paste the exact
generated-API-client method signature(s) it must call, read fresh from
frontend/src/app/core/api/generated-client.ts.>

## 4. TASK
1. <Exact, numbered steps, with exact file paths and selector/class names.>
2. <...>

## 5. HARD CONSTRAINTS
- No hand-written HttpClient calls bypassing the generated API client.
- No localStorage/sessionStorage use for auth tokens.
- Every async operation shows loading/empty/error states per the shared design system (P-1011).
<plus any gap-specific constraint>

## 6. FORBIDDEN PATTERNS
<Anti-Stub Clause, verbatim, with special attention to its frontend-specific lines: no
"coming soon" templates, no empty (click) handlers, no services returning of(mockData).>

## 7. VERIFICATION
```bash
cd store-os-cloud/frontend
ng test --include='<affected spec files>' --watch=false
ng build --configuration production
```

## 8. FAILURE PROTOCOL
<Standard failure protocol, verbatim, referencing this remediation prompt's own ID.>

## 9. REPORT
Append to docs/BUILD_LOG.md and docs/ARTIFACTS.md exactly as every other prompt does.
```

---

## Template 3 — Security / RLS / Permission Gap

```
# REMEDIATION — <short title>

**Phase:** Remediation &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION
You are closing a SECURITY gap in the StoreOS Cloud build in the Oh My Pi IDE.
THE GAP: <describe the exact isolation/permission failure — e.g. a new table missing an RLS
policy, a command missing IRequiresPermission/IHighImpactOperation, a query filter omitted
from QueryFilters.ApplyAll>.

## 2. WORLD STATE
- The full backend (through prompt <ID>) is otherwise complete; RLS coverage was previously
  100% per RlsCoverageTests (P-111) but a NEW gap was introduced or discovered at: <exact
  table/entity/endpoint>.
- Do not weaken any EXISTING RLS policy or query filter while fixing this — only ADD the
  missing coverage.

## 3. CONTEXT BLOCK
<Paste the exact current CREATE TABLE / migration for the affected table. Paste
QueryFilters.ApplyAll's current full content. Paste the RLS Archetype (A, B, or C) this table's
shape requires, copied verbatim from docs/ARTIFACTS.md's P-108/P-110/P-414 records.>

## 4. TASK
1. Add the missing RLS policy / query filter / permission-interface implementation exactly
   matching the correct archetype for this table's shape.
2. Add or extend a migration (never edit an already-applied migration in place) if a schema
   change is needed.
3. Add a permission-code-usage check per P-115's fixed 17-code catalog if a permission gap is
   involved.

## 5. HARD CONSTRAINTS
<Global Invariants block, verbatim, with special emphasis on the RLS/tenancy lines:
never write a policy with "OR current_setting(...) IS NULL"; always FORCE ROW LEVEL SECURITY;
always use current_setting(..., true) with missing_ok=true.>

## 6. FORBIDDEN PATTERNS
<Anti-Stub Clause, verbatim.>

## 7. VERIFICATION
```bash
cd store-os-cloud
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter <any new/affected test>
```
`RlsCoverageTests` must show 3 passed, 0 failed, confirming 100% coverage is restored.

## 8. FAILURE PROTOCOL
<Standard failure protocol, verbatim, referencing this remediation prompt's own ID. NEVER
weaken a test or a policy to force a pass — this is a security-critical remediation; if
genuinely blocked, escalate rather than patch around the gap.>

## 9. REPORT
Append to docs/BUILD_LOG.md and docs/ARTIFACTS.md, explicitly flagging this as a
SECURITY remediation with the exact date/prompt-id it was discovered under.
```

---

## Template 4 — Test Coverage Gap (Missing Invariant Test)

```
# REMEDIATION — <short title>

**Phase:** Remediation &nbsp;|&nbsp; **Minimum executor reasoning level:** <med|high>

## 1. ROLE & MISSION
You are adding MISSING TEST COVERAGE to the StoreOS Cloud build in the Oh My Pi IDE.
THE GAP: <name the specific Requirements §48 invariant, or Plan §16 testing-table row, that
currently has no automated test proving it — or an existing test that is weaker than the
manifest requires (e.g. asserts internal consistency only, not exact hand-computed values)>.

## 2. WORLD STATE
- The underlying BEHAVIOR this test should cover already exists and works (this is a test-only
  gap, not a behavior gap — if the behavior itself is also missing, use Template 1 instead).
- The relevant production code lives at: <exact file path(s)>.

## 3. CONTEXT BLOCK
<Paste the exact production code this test must exercise. Paste any existing, related test
file's current content so the new test is added consistently alongside it, not duplicating
fixture/setup logic unnecessarily.>

## 4. TASK
1. Write <exact test class/method name> in <exact test file path> asserting <exact numbered
   cases, each with hand-computed expected values — show the arithmetic/reasoning in a comment
   above each assertion>.
2. Ensure the test fails against a DELIBERATELY broken version of the production code (verify
   this by temporarily breaking the logic, confirming the new test catches it, then restoring
   the correct logic) — this proves the test is not vacuously passing.

## 5. HARD CONSTRAINTS
- Every numeric assertion uses a precisely hand-computed expected value, never a
  "should be greater than" or "should not be null" style weak assertion, unless the property
  being tested is genuinely non-numeric.
<Global Invariants block, verbatim, where relevant to the code under test.>

## 6. FORBIDDEN PATTERNS
<Anti-Stub Clause, verbatim — this applies to test code too: no empty test bodies, no
[Fact(Skip=...)], no assertions that always pass regardless of behavior.>

## 7. VERIFICATION
```bash
cd store-os-cloud
dotnet test <exact project> --filter <exact new test class name>
```

## 8. FAILURE PROTOCOL
<Standard failure protocol, verbatim, referencing this remediation prompt's own ID.>

## 9. REPORT
Append to docs/BUILD_LOG.md and docs/ARTIFACTS.md, noting which Requirements §48 invariant or
Plan §16 testing-table row is now covered.
```

---

## Template 5 — Generic Gap (fallback when Templates 1–4 don't fit)

```
# REMEDIATION — <short title>

**Phase:** Remediation &nbsp;|&nbsp; **Minimum executor reasoning level:** <low|med|high>

## 1. ROLE & MISSION
You are closing a gap in the StoreOS Cloud build in the Oh My Pi IDE, found during the
Completeness Audit (P-1205) or a later review.
THE GAP: <describe the gap in full — what is wrong, where, why it matters, and which part of
the product requirements or implementation plan it relates to. Be as specific as every other
prompt in this manifest: exact file paths, exact expected vs. actual behavior, exact error
messages if any.>

## 2. WORLD STATE
- <List what already exists and works, precisely, as of the point this gap was found.>
- <List anything that must NOT be touched while fixing this gap.>

## 3. CONTEXT BLOCK
<Paste every piece of exact, current, verbatim context the executor needs: source code, schema,
API contracts, test output, error logs — read fresh from the repository, never from a possibly
stale record in this manifest.>

## 4. TASK
1. <Numbered, exact steps.>
2. <...>

## 5. HARD CONSTRAINTS
<Global Invariants block, verbatim, plus any gap-specific constraint.>

## 6. FORBIDDEN PATTERNS
<Anti-Stub Clause, verbatim.>

## 7. VERIFICATION
```bash
<exact commands and exact expected outcomes>
```

## 8. FAILURE PROTOCOL
<Standard failure protocol, verbatim, referencing this remediation prompt's own ID
(e.g. "REMEDIATION-<NNN>").>

## 9. REPORT
Append to docs/BUILD_LOG.md and docs/ARTIFACTS.md exactly as every other prompt does.
```
