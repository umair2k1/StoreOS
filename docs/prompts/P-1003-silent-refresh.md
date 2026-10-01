# P-1003 — Silent refresh on APP_INITIALIZER + refresh flow

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1003** of the StoreOS Cloud build ("Silent refresh on APP_INITIALIZER + refresh flow") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1002 complete: AuthSignalStore and the full reactive 401-refresh
  interceptor both work and are tested.
- No bootstrap-time silent refresh exists yet - on a hard page reload, the in-memory
  AuthSignalStore is empty (by design), so the app must attempt a silent refresh using the
  HttpOnly cookie BEFORE rendering any authenticated UI, per Plan Section 12.2 point 3.

## 3. CONTEXT BLOCK

Create `frontend/src/app/core/auth/silent-refresh.ts`:

```typescript
import { inject } from '@angular/core';
import { AuthRefreshService } from './auth-refresh.service';
import { firstValueFrom, catchError, of } from 'rxjs';

export function silentRefreshInitializer(): () => Promise<void> {
  const refreshService = inject(AuthRefreshService);
  return async () => {
    try {
      await firstValueFrom(refreshService.refresh());
    } catch {
      // No valid refresh cookie present (first visit, or fully logged out) - this is a normal,
      // expected outcome, not an error condition. The app proceeds unauthenticated; the auth
      // guard (P-1005) will redirect to /login when a protected route is visited.
    }
  };
}
```

Wire into `frontend/src/app/app.config.ts` via Angular's `APP_INITIALIZER` (or the newer
`provideAppInitializer` in Angular 18):

```typescript
import { provideAppInitializer } from '@angular/core';
// ...
export const appConfig: ApplicationConfig = {
  providers: [
    // ... existing providers
    provideAppInitializer(() => silentRefreshInitializer()()),
  ]
};
```

This MUST complete (success or failure) BEFORE the router activates the first route, so a
protected route's guard sees accurate authentication state immediately on load, never a
false-negative "not authenticated" flash before the cookie-based refresh has had a chance to
run.

## 4. TASK

1. Create `silent-refresh.ts` exactly as shown in
   `frontend/src/app/core/auth/`.
2. Wire it into `app.config.ts` via `provideAppInitializer`, confirming it blocks router
   activation until complete.
3. Write `frontend/src/app/core/auth/silent-refresh.spec.ts`: (a) when the mock HTTP backend
   returns a successful refresh response, `AuthSignalStore.isAuthenticated()` is `true`
   immediately after the initializer resolves; (b) when the mock backend returns a 401 (no
   valid cookie), the initializer resolves WITHOUT throwing, and `isAuthenticated()` remains
   `false`; (c) using Angular's `TestBed` with a router harness, confirm no route activates
   before the initializer's promise resolves (a slow-refresh scenario: delay the mock response
   and confirm the router's initial navigation is correctly held up until it completes).

## 5. HARD CONSTRAINTS

- The initializer must NEVER let a rejected/failed refresh propagate as an
  unhandled promise rejection or crash app bootstrap - a failed silent refresh (no cookie, or
  an expired one) is a normal, silent, successful no-op from the app's perspective.
- Router activation must wait for this initializer - verify this explicitly with a timing-based
  test, not just an assumption based on `provideAppInitializer`'s documented behavior.

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
ng test --include='**/silent-refresh.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1003 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1003 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1003` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `silentRefreshInitializer` function and its `provideAppInitializer`
wiring, and confirmation router activation is correctly gated on its completion.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1003: <one-line summary>"` only after verification is
   fully green.
