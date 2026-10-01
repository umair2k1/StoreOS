# P-512 — Checkout concurrency test suite (last-unit race, IMEI double-sell, parallel invoice numbering)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-512** of the StoreOS Cloud build ("Checkout concurrency test suite (last-unit race, IMEI double-sell, parallel invoice numbering)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-510, P-511 complete: full checkout handler with retry wrapper exists.
- Individual pieces (`InventoryStockConcurrencyTests` P-406, `ImeiDoubleSaleConcurrencyTests`
  P-408, `InvoiceSequenceConcurrencyTests` P-501, `CheckoutSerializationRetryTests` P-511) each
  proved their OWN layer's concurrency safety in isolation. This prompt proves the FULL,
  END-TO-END checkout path is safe under concurrency, exercising all three mechanisms
  TOGETHER through real HTTP-level (or `IMediator.Send`-level) concurrent checkout calls —
  this is the definitive test suite Requirements §31/Invariant 1/2 and Plan §16's "concurrency"
  test row describe.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** The concurrency suite runs against the APP role connection (P-108 fixture). The three scenarios stay as written; add a fourth: two parallel requests with the same `Idempotency-Key` produce exactly one sale (see P-511 tests).

Create `backend/tests/StoreOS.ConcurrencyTests/EndToEndCheckoutConcurrencyTests.cs`
with three definitive scenarios, each using `WebApplicationFactory` + real authenticated HTTP
`POST /api/pos/checkout` calls (this requires P-513's controller endpoint — if sequenced
strictly, P-512 technically depends on P-513's route existing; note this cross-dependency
explicitly and, if the endpoint isn't wired yet at this point in strict prompt order, drive the
test through `IMediator.Send(new CheckoutCommand(...))` directly against a properly-scoped
`IServiceScope` instead of real HTTP, which exercises the identical `CheckoutHandler`+
`SerializationRetryBehavior` pipeline without needing the controller route — use THIS approach
so P-512 has no hard ordering dependency on P-513):

**Scenario 1 — Last unit race**: seed exactly 1 unit of a non-IMEI-tracked `Accessory` in
stock. Launch 5 CONCURRENT tasks, each its own scope, each attempting to check out 1 unit of
that SAME product. Assert: EXACTLY 1 succeeds with a 201-equivalent result; the other 4 each
receive a clean `insufficient_stock` `DomainValidationException` (422-equivalent), NEVER a raw
unhandled exception; final `branch_inventory.stock_qty == 0`.

**Scenario 2 — IMEI double-sell**: seed exactly 1 `InStock` IMEI for a `NewPhone`. Launch 3
concurrent tasks, each attempting to check out THAT SAME IMEI. Assert: exactly 1 succeeds; the
IMEI ends `Sold` with a valid (non-empty-GUID) `sale_item_id` pointing at the WINNING sale's
item; the other 2 tasks fail cleanly (`imei_not_available` or `insufficient_stock`, whichever
surfaces first depending on timing — assert on the SET of acceptable failure codes, not a
single hardcoded one, and document which ones you actually observed).

**Scenario 3 — Parallel invoice numbering across concurrent checkouts**: seed enough stock for
20 units of a product. Launch 20 CONCURRENT successful checkouts (1 unit each, guaranteed
non-competing stock). Assert: all 20 succeed, and all 20 resulting `InvoiceNumber` values are
UNIQUE (query the `sales` table afterward and assert `COUNT(DISTINCT invoice_number) == 20`),
with sequence numbers forming a consecutive or near-consecutive run (allow for the documented
P-511 gap possibility, but assert no DUPLICATE numbers under any circumstance — duplicates
would be a correctness failure, gaps are an accepted tradeoff already documented).

Each scenario must run against a FRESH `PostgresTestFixture`-backed database (reuse from
P-111) to avoid cross-scenario interference, and must use the `TestAuthHandler`-equivalent
scoped `IAccessScope` setup pattern established since P-106, ensuring every concurrent task
has ITS OWN correctly-scoped `IServiceScope`/`StoreOsDbContext`/transaction (simulating truly
independent concurrent HTTP requests, not accidentally sharing a single DbContext instance
across tasks, which would be an invalid test setup masking real concurrency bugs).

## 4. TASK

1. Write all three scenarios in
   `StoreOS.ConcurrencyTests/EndToEndCheckoutConcurrencyTests.cs` exactly as described, driving
   through `IMediator.Send` in independently-scoped `IServiceScope`s per concurrent task (per
   the no-hard-dependency-on-P-513 decision above).
2. Run each scenario at least 5 times in a loop within the test (concurrency bugs are often
   flaky/timing-dependent; a single pass proves little) — assert the invariant holds on EVERY
   iteration, not just once. If any iteration fails, the test fails.
3. Document, in `docs/BUILD_LOG.md`, the exact failure codes empirically observed in
   Scenario 2's losing tasks across your test runs.

## 5. HARD CONSTRAINTS

- Every concurrent task MUST use its OWN `IServiceScope` (hence its
  own `StoreOsDbContext`, own `IAccessScope`, own ambient transaction) — sharing a single
  `DbContext` across concurrent `Task.Run` calls is NOT a valid way to test concurrency (EF
  DbContext is not thread-safe) and would produce meaningless or misleading results.
- Run each scenario multiple times (at least 5 iterations) in the test itself — a single
  successful run does not prove the race is actually handled, since race conditions are
  inherently timing-sensitive and can pass by luck once.

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
dotnet test backend/tests/StoreOS.ConcurrencyTests --filter EndToEndCheckoutConcurrencyTests
# Expected: all 3 scenarios pass across all 5 iterations each (15 total scenario-runs green).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-512 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-512 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-512` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: confirmation all three scenarios pass reliably across repeated iterations, and
the exact set of failure codes empirically observed for losing concurrent checkout attempts in
each scenario.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-512: <one-line summary>"` only after verification is
   fully green.
