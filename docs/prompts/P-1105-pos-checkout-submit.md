# P-1105 — POS: checkout submission + invoice print flow (server PDF, printing-never-rolls-back toast)

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1105** of the StoreOS Cloud build ("POS: checkout submission + invoice print flow (server PDF, printing-never-rolls-back toast)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1104 complete: payment panel and finance dialog both work.
- POST /api/pos/checkout (P-513, mandatory Idempotency-Key - already auto-attached by
  P-1004's idempotencyInterceptor), GET /api/sales/{id}/invoice.pdf (P-809) both exist.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/pos/checkout.service.ts`: assembles the full
CheckoutCommand from CartStateService + CustomerSelectComponent's selection +
PaymentPanelComponent's payment lines + FinanceDialogComponent's finance details (if used) +
any invoice-level discount field, calls the generated client's checkout method, and on success:
clears the cart, shows a success toast, and triggers a PDF fetch+print via
`window.open(blobUrl)` or an iframe-based print flow. Per Requirements Section 40's "Printer
failure" rule and Plan's explicit invariant: if the PDF fetch/print step fails AFTER a
successful checkout response, the sale is ALREADY safely recorded server-side - show a toast
reading "Sale completed (Invoice #X) — printing failed" with a "Reprint" action button, NEVER
imply the sale itself failed or was rolled back.

## 4. TASK

1. Create `CheckoutService` assembling and submitting the full `CheckoutCommand`.
2. Implement the post-checkout PDF print flow with the explicit success-despite-print-failure
   toast behavior described.
3. Wire a "Checkout" button/hotkey (F4, per P-1101's reserved hotkey) triggering the flow.
4. Write `checkout.service.spec.ts`: (a) a successful checkout response clears the cart and
   shows a success toast; (b) a successful checkout response FOLLOWED BY a failed PDF fetch
   (mock the print HTTP call to fail) still shows the sale-succeeded toast, with a distinct
   "printing failed, reprint available" message, never an error framing that suggests the sale
   itself failed; (c) a genuine checkout FAILURE (e.g. 422 insufficient_stock) shows an error
   toast and does NOT clear the cart, letting the user correct and retry.

## 5. HARD CONSTRAINTS

- A PDF/print failure AFTER a successful checkout must NEVER be presented to the user as a
  sale failure - the toast must clearly communicate the sale succeeded and offer a reprint
  action, matching Requirements Section 40 and the Plan's explicit invariant.

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
ng test --include='**/checkout.service.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1105 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1105 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1105` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CheckoutService` structure and the exact print-failure-vs-sale-failure toast
distinction.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1105: <one-line summary>"` only after verification is
   fully green.
