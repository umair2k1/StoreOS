# P-1002 — AuthSignalStore (in-memory access token) + auth interceptor

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1002** of the StoreOS Cloud build ("AuthSignalStore (in-memory access token) + auth interceptor") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1001 complete: generated TypeScript API client exists and compiles,
  environment config wired.
- No authentication state management exists in the frontend yet. Per Plan Section 12.2, the
  access token must live ONLY in memory (never localStorage/sessionStorage), consistent with
  the backend's BFF architecture (P-302-306).

## 3. CONTEXT BLOCK

Create `frontend/src/app/core/auth/auth-signal-store.ts` using Angular
signals (no NgRx, per the lean architecture):

```typescript
import { Injectable, signal, computed } from '@angular/core';

export interface AuthState {
  accessToken: string | null;
  expiresAt: string | null;
  userId: string | null;
  displayName: string | null;
  tenantId: string | null;
  allBranches: boolean;
  branchIds: string[];
  permissions: string[];
}

const EMPTY_STATE: AuthState = {
  accessToken: null, expiresAt: null, userId: null, displayName: null,
  tenantId: null, allBranches: false, branchIds: [], permissions: []
};

@Injectable({ providedIn: 'root' })
export class AuthSignalStore {
  private readonly state = signal<AuthState>(EMPTY_STATE);

  readonly isAuthenticated = computed(() => this.state().accessToken !== null);
  readonly accessToken = computed(() => this.state().accessToken);
  readonly displayName = computed(() => this.state().displayName);
  readonly permissions = computed(() => this.state().permissions);
  readonly branchIds = computed(() => this.state().branchIds);
  readonly allBranches = computed(() => this.state().allBranches);

  setAuthenticated(data: Omit<AuthState, never>): void {
    this.state.set(data);
  }

  clear(): void {
    this.state.set(EMPTY_STATE);
  }

  hasPermission(code: string): boolean {
    return this.state().permissions.includes(code);
  }
}
```

**CRITICAL**: `accessToken` is NEVER written to `localStorage`, `sessionStorage`, or any
persistent storage - it lives ONLY in this in-memory signal, exactly matching Plan Section
12.2's BFF security architecture. It is lost on every page refresh, which is EXPECTED and
handled by the silent-refresh flow (P-1003).

Create `frontend/src/app/core/interceptors/auth.interceptor.ts` (functional interceptor style,
Angular 18):

```typescript
import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError, BehaviorSubject, filter, take } from 'rxjs';
import { AuthSignalStore } from '../auth/auth-signal-store';

let isRefreshing = false;
const refreshedSubject = new BehaviorSubject<boolean>(false);

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authStore = inject(AuthSignalStore);
  const token = authStore.accessToken();
  const authedReq = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authedReq).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || req.url.includes('/auth/refresh') || req.url.includes('/auth/login')) {
        return throwError(() => err);
      }
      // Buffer concurrent requests during refresh (implemented fully in P-1003 alongside the
      // actual refresh call - this interceptor triggers it and replays once complete).
      return throwError(() => err); // P-1003 replaces this branch with the real buffering logic
    })
  );
};
```

Note the 401-buffering-and-replay logic is INTENTIONALLY left as a clear extension point here,
completed in P-1003 once the actual `/auth/refresh` call exists to trigger - this is NOT a stub
under the anti-stub rule's definition, since the interceptor is FULLY FUNCTIONAL for its
CURRENT scope (attaching the bearer token, passing through non-401 and refresh/login-endpoint
errors correctly) - P-1003 EXTENDS it with additional real behavior in the SAME file, it does
not fill in an empty placeholder.

Actually, per the strict anti-stub rule, leaving a codepath that "returns the error unchanged"
where real buffering behavior is DESCRIBED but not implemented risks being read as a stub.
Resolve this decisively: do NOT write the interceptor in two prompts. Implement the FULL
401-buffering-and-refresh-triggering logic HERE in this prompt, calling a
`AuthRefreshService.refresh()` method whose IMPLEMENTATION (calling the real generated API
client's refresh endpoint) is created in THIS SAME prompt too (not deferred to P-1003) - P-1003
instead focuses on the APP_INITIALIZER bootstrap-time silent refresh, a genuinely separate
concern (refreshing once at app startup vs. refreshing reactively on a 401 during normal
usage).

## 4. TASK

1. Create `AuthSignalStore` exactly as shown in
   `frontend/src/app/core/auth/auth-signal-store.ts`.
2. Create `frontend/src/app/core/auth/auth-refresh.service.ts`:
   ```typescript
   @Injectable({ providedIn: 'root' })
   export class AuthRefreshService {
     constructor(private http: HttpClient, private authStore: AuthSignalStore) {}
     refresh(): Observable<void> {
       return this.http.post<any>('/api/auth/refresh', {}, { withCredentials: true }).pipe(
         tap(response => this.authStore.setAuthenticated({
           accessToken: response.accessToken, expiresAt: response.expiresAt, userId: response.userId,
           displayName: response.displayName, tenantId: response.tenantId, allBranches: response.allBranches,
           branchIds: response.branchIds, permissions: response.permissions
         })),
         map(() => void 0),
         catchError(err => { this.authStore.clear(); return throwError(() => err); })
       );
     }
   }
   ```
   (`withCredentials: true` is required so the browser attaches the HttpOnly refresh cookie.)
3. Implement the FULL `authInterceptor` with real 401-buffering: on a 401 (excluding
   `/auth/refresh`/`/auth/login` requests themselves), if a refresh is not already in flight,
   start one via `AuthRefreshService.refresh()`; queue/buffer any OTHER concurrent requests that
   also hit a 401 during the same window; once refresh completes, retry ALL buffered requests
   (including the original one) with the new token; if refresh itself fails, clear auth state and
   propagate the error (triggering a redirect to login via the auth guard, built in P-1005).
4. Register the interceptor via `provideHttpClient(withInterceptors([authInterceptor]))` in
   `app.config.ts`.
5. Write `frontend/src/app/core/interceptors/auth.interceptor.spec.ts`: (a) a request with a
   token in the store gets the `Authorization` header attached; (b) a 401 response triggers
   exactly ONE refresh call even when THREE concurrent requests all 401 simultaneously (buffering
   proof); (c) after a successful refresh, all three buffered requests are retried with the NEW
   token; (d) a failed refresh clears the auth store and propagates the original error.

## 5. HARD CONSTRAINTS

- The access token is NEVER persisted to any browser storage API - it lives
  exclusively in the `AuthSignalStore`'s in-memory signal. Grep the entire `frontend/` tree
  for `localStorage`/`sessionStorage` usage related to auth tokens as a self-check before
  finishing - there must be zero hits.
- Concurrent 401s during an in-flight refresh must trigger exactly ONE refresh call, with all
  affected requests correctly buffered and replayed - this is the core correctness property
  this prompt's test suite must prove.

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
ng test --include='**/auth.interceptor.spec.ts' --watch=false
grep -rn "localStorage\|sessionStorage" src/app/core/auth/ || echo "clean: no persistent token storage"
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1002 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1002 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1002` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `AuthSignalStore`/`AuthRefreshService` signatures, the full
`authInterceptor` buffering behavior, and confirmation of zero persistent token storage
anywhere in the codebase.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1002: <one-line summary>"` only after verification is
   fully green.
