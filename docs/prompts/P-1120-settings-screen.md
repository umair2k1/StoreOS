# P-1120 — Settings screen: shop info, tax, bank, finance providers, theme, repair flag

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1120** of the StoreOS Cloud build ("Settings screen: shop info, tax, bank, finance providers, theme, repair flag") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1119 complete: reports screen works.
- PATCH /api/settings/{key} (P-1007), finance-providers CRUD (P-516),
  GET/PATCH /api/settings/repairs-enabled (P-708), logo upload (P-1009) all exist.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** add a 'My account' section with a 'Change password' form calling `POST /api/auth/change-password` (P-313). On 204 clear the in-memory auth state and navigate to `/login` (the server has revoked all sessions).

Create `frontend/src/app/features/settings/settings.component.ts` with tabbed sections:
Shop Info (name/address/email/phone/GSTIN + LogoUploaderComponent reused from P-1009), Tax
(default GST rate), Bank (name/account name/account number/IFSC/branch), Finance Providers
(list + add/remove, reusing P-1104's provider-management pattern), Theme (a simple named-theme
selector persisting a CSS class/data-attribute choice - implement at least 2 real themes,
light and dark, toggled via a `data-theme` attribute on `<html>`, backed by CSS custom
properties already established in the P-1011 design-system work), and Repairs (the enable/
disable toggle, `*pzCan="'settings:manage'"`).

## 4. TASK

1. Create `SettingsComponent` with all six tabbed sections exactly as described.
2. Implement the light/dark theme toggle with real CSS custom-property-based styling (not just
   a UI control that does nothing).
3. Write `settings.component.spec.ts`: each tab's save action calls the correct
   `PATCH /api/settings/{key}` with the correct key/value shape; the repairs toggle is hidden
   without `settings:manage`; the theme toggle actually changes the `data-theme` attribute on
   the document root.

## 5. HARD CONSTRAINTS

- The theme toggle must be a REAL, working feature (actually changing visible styling via
  CSS custom properties), not a decorative no-op control.

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
ng test --include='**/settings.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1120 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1120 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1120` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `SettingsComponent` structure and the real light/dark theme implementation.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1120: <one-line summary>"` only after verification is
   fully green.
