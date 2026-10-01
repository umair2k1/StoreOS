# P-1104 — POS: payment panel (cash/UPI/card/credit/split) + finance dialog

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1104** of the StoreOS Cloud build ("POS: payment panel (cash/UPI/card/credit/split) + finance dialog") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1103 complete: cart editing fully works.
- CheckoutFinanceDetails (FinanceProvider, FinancerName, DownPayment, TenureMonths,
  AnnualInterestRate) documented in docs/ARTIFACTS.md's P-504/515 records. GET
  /api/settings/finance-providers (P-516) exists for the provider dropdown.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/pos/payment/payment-panel.component.ts`: supports
multiple simultaneous payment lines (split payment, Requirements Section 9.2), each with a mode
dropdown (Cash/UPI/Card/Credit) and amount field, live-summing against the cart's grand total to
show the remaining balance. Create `frontend/src/app/features/pos/payment/finance-dialog
.component.ts`: a modal (via PzDialogService) with finance-provider typeahead (backed by
GET /api/settings/finance-providers, with an "add new provider" inline action calling
POST /api/settings/finance-providers per P-516), down payment, tenure, and interest rate
inputs, showing a LIVE EMI PREVIEW computed client-side using the exact same formula as
FinanceEngine.CalculateEmi (P-204) - reimplement the pure zero-interest and positive-interest
formulas in TypeScript (a small `emi-calculator.ts` pure function) so the preview updates
instantly without a round-trip, while the ACTUAL persisted EMI is always the server's own
recomputation at checkout time (never trust the client preview for the persisted record).

## 4. TASK

1. Create `PaymentPanelComponent` with split-payment support and live balance summation.
2. Create `emi-calculator.ts` (pure TypeScript function mirroring FinanceEngine.CalculateEmi's
   exact zero-interest and positive-interest formulas, including the same DecimalPow-equivalent
   integer-exponent loop rather than `Math.pow`, for consistency with the backend's decimal-safe
   approach even though JS numbers are floating-point - document this as a KNOWN, ACCEPTABLE
   minor precision difference for a PREVIEW ONLY, since the server's decimal computation is
   authoritative for the persisted value).
3. Create `FinanceDialogComponent` using `emi-calculator.ts` for the live preview.
4. Write `emi-calculator.spec.ts`: the same test cases from FinanceEngineTests (P-204),
   confirming the JS preview matches the C# result to at least 2 decimal places for typical
   inputs.
5. Write `payment-panel.component.spec.ts` and `finance-dialog.component.spec.ts` covering
   split-payment summation and the add-new-provider inline flow.

## 5. HARD CONSTRAINTS

- The client-side EMI preview is explicitly a PREVIEW ONLY - the server always recomputes
  the authoritative EMI via FinanceEngine at checkout time (P-515); document this distinction
  clearly in code comments.

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
cd store-os-cloud/frontend
ng test --include='**/emi-calculator.spec.ts' --include='**/payment-panel.component.spec.ts' --include='**/finance-dialog.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1104 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1104 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1104` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `PaymentPanelComponent`/`FinanceDialogComponent`/`emi-calculator.ts` structures and
the preview-vs-authoritative EMI distinction.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1104: <one-line summary>"` only after verification is
   fully green.
