# P-1011 — Shared design system: pz-table, pz-form-field, pz-dialog, money pipe, date pipe, empty/loading/error states, toast service

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1011** of the StoreOS Cloud build ("Shared design system: pz-table, pz-form-field, pz-dialog, money pipe, date pipe, empty/loading/error states, toast service") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1010 complete: login/lock-screen work.
- No shared design-system components exist yet - every Phase 11 feature screen needs
  pz-table, pz-form-field, pz-dialog, money pipe, date pipe, empty/loading/error states, and a
  toast service, per Plan Section 12.2's `shared/` folder listing.

## 3. CONTEXT BLOCK

Per the `frontend-design` skill available in this environment, review
it for styling/design-token guidance before building these components (view
`/mnt/skills/public/frontend-design/SKILL.md` first) to ensure a coherent, intentional visual
language rather than generic defaults.

Create the following in `frontend/src/app/shared/`, each a genuinely complete, functional
standalone component/pipe/service (not a placeholder):

- `pipes/money.pipe.ts` (`PzMoneyPipe`, `standalone: true`, transforms a `number` into
  `en-IN`-formatted INR currency, e.g. `formatCurrency(value, 'en-IN', '₹', 'INR', '1.2-2')`
  via Angular's `CurrencyPipe` composition or `Intl.NumberFormat('en-IN', { style: 'currency',
  currency: 'INR' })`).
- `pipes/ist-date.pipe.ts` (`PzDatePipe`, formats a date/timestamp using `en-IN` date
  conventions, e.g. `DD-MMM-YYYY`).
- `components/pz-table/pz-table.component.ts` (generic, typed via Angular's input generics or a
  simple `@Input() rows: T[]`/`@Input() columns: {key: string; label: string}[]`
  content-projected table with sortable-header click outputs).
- `components/pz-form-field/pz-form-field.component.ts` (a labeled wrapper around
  `ng-content` for form controls, with an error-message slot bound to a passed-in
  `FormControl`'s validation state).
- `components/pz-dialog/pz-dialog.component.ts` + `pz-dialog.service.ts` (a simple
  signal-driven modal service: `open(component, data)` / `close(result)`, rendered via
  Angular CDK's `Overlay` - add `@angular/cdk` as a new pinned dependency now,
  version matching your Angular 18 install exactly, e.g. `18.2.13`).
- `components/empty-state/empty-state.component.ts`, `components/loading-state/loading-state
  .component.ts`, `components/error-state/error-state.component.ts` (small, reusable, each
  accepting a message/icon input).
- `services/toast.service.ts` (signal-based queue of toast messages with severity levels
  `info`/`success`/`warning`/`error`, auto-dismiss timers, and a `showWithCorrelationId(message,
  correlationId)` variant per Plan Section 12.2's error-toast-with-correlation-id requirement)
  plus a `components/toast-container/toast-container.component.ts` rendering the queue,
  mounted once in `ShellComponent`.

## 4. TASK

1. Add `@angular/cdk` (exact version matching the installed Angular version) as
   a pinned dependency.
2. Create every component/pipe/service listed, each FULLY FUNCTIONAL (not placeholder markup),
   in `frontend/src/app/shared/`.
3. Mount `ToastContainerComponent` once in `ShellComponent` (P-1006).
4. Write a `.spec.ts` for EVERY item created: `money.pipe.spec.ts` (exact `en-IN`/INR formatting
   assertions for several values including zero, negative, and large numbers); `ist-date.pipe
   .spec.ts`; `pz-table.component.spec.ts` (renders rows/columns correctly, emits a sort event);
   `pz-form-field.component.spec.ts` (shows an error message when the bound control is invalid
   and touched); `pz-dialog.service.spec.ts` (opening/closing updates state correctly);
   `empty-state`/`loading-state`/`error-state` `.component.spec.ts` (render their inputs);
   `toast.service.spec.ts` (enqueueing a toast makes it appear in the queue; auto-dismiss removes
   it after the configured timeout, testable via Angular's fakeAsync/tick utilities).

## 5. HARD CONSTRAINTS

- Every one of these shared components must be genuinely complete and usable by
  Phase 11 feature screens - no placeholder markup, no "coming soon" text, no empty click
  handlers anywhere in this shared library, since it is the FOUNDATION every later screen
  builds on.
- Follow the `frontend-design` skill's guidance for visual coherence rather than unstyled
  browser defaults.

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
ng test --include='**/shared/**/*.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1011 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1011 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1011` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the full list of shared component/pipe/service signatures created, since
every Phase 11 prompt imports from this shared library extensively.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1011: <one-line summary>"` only after verification is
   fully green.
