# P-1010 — Login screen + lock screen components

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1010** of the StoreOS Cloud build ("Login screen + lock screen components") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1009 complete: logo upload works end-to-end.
- No login screen exists yet - `AuthRefreshService`/`AuthSignalStore` (P-1002) exist but no UI
  calls the actual login endpoint.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the login screen learns the tenant (name + logo) from `GET /api/tenant/public-profile` (add it to `ManifestController` in P-1008: returns `{tenantId, name}` from `ITenantResolver.ResolveFromHostAsync`, 404 when unknown) and posts `{email, password}` — the backend resolves the tenant from the Host header, so the UI never needs the tenant GUID. Ignore any text below that asks the user to type a tenant id.

Create `frontend/src/app/features/auth/login.component.ts` (standalone,
reactive forms):

```typescript
import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html'
})
export class LoginComponent {
  private fb = inject(FormBuilder);
  private http = inject(HttpClient);
  private authStore = inject(AuthSignalStore);
  private router = inject(Router);

  form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    tenantId: ['', Validators.required] // per backend's LoginCommand shape (P-303) requiring
                                          // an explicit tenant id in this non-subdomain-routed
                                          // local/dev/testing context
  });
  isSubmitting = signal(false);
  errorMessage = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) return;
    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.http.post<any>('/api/auth/login', this.form.getRawValue(), { withCredentials: true }).subscribe({
      next: (response) => {
        this.authStore.setAuthenticated({
          accessToken: response.accessToken, expiresAt: response.expiresAt, userId: response.userId,
          displayName: response.displayName, tenantId: response.tenantId, allBranches: response.allBranches,
          branchIds: response.branchIds, permissions: response.permissions
        });
        this.isSubmitting.set(false);
        this.router.navigateByUrl('/pos');
      },
      error: (err) => {
        this.isSubmitting.set(false);
        this.errorMessage.set(err?.error?.title ?? 'Login failed. Please check your credentials.');
      }
    });
  }
}
```

Create a companion `login.component.html` template with the form fields, a submit button
disabled while `isSubmitting()`, and an error message region bound to `errorMessage()`.

Create `frontend/src/app/features/auth/lock-screen.component.ts` - a simpler re-authentication
prompt (password only, no email/tenant fields) shown when a session needs re-confirmation
(e.g. after an extended idle period - the TRIGGER for showing this is a future enhancement; for
THIS prompt, build the component itself as a complete, working, reusable piece, callable from
anywhere, even if nothing currently triggers it automatically - it is NOT a stub, since its
form/submit logic is fully functional, it simply isn't wired to an automatic idle-timer trigger
in this prompt's scope).

## 4. TASK

1. Create `LoginComponent` (`.ts` + `.html` + `.scss`) exactly as shown/described
   in `frontend/src/app/features/auth/`.
2. Create `LockScreenComponent` similarly - fully functional password-re-entry form, callable
   on demand (e.g. via a route or a manually-invoked overlay), calling
   `POST /api/auth/login` (or a lighter re-auth endpoint if one existed - none does per the
   current backend scope, so reuse the SAME login endpoint, pre-filling the known
   email/tenantId from the current `AuthSignalStore` state and asking only for the password).
3. Wire `/login` route (unguarded) and expose `LockScreenComponent` via a route or an injectable
   overlay service (`LockScreenOverlayService` - simple: a signal-based visibility flag
   toggled by a service, rendered conditionally in `ShellComponent`).
4. Write `login.component.spec.ts`: (a) submitting valid credentials (mocked 200 response)
   populates `AuthSignalStore` and navigates to `/pos`; (b) submitting invalid credentials
   (mocked 401) displays the error message and does NOT navigate; (c) the form is invalid
   (submit button effectively disabled via the `disabled` binding, or `submit()` no-ops) when
   any required field is empty.
5. Write `lock-screen.component.spec.ts`: successful re-auth with the correct password
   re-populates `AuthSignalStore`; wrong password shows an error.

## 5. HARD CONSTRAINTS

- Both components use `withCredentials: true` on their login HTTP calls, since
  the backend sets the refresh-token cookie on this same response (P-304) - omitting this flag
  would silently break the cookie exchange.
- Every dynamic branding note from P-1006 applies here too - neither screen shows any
  "StoreOS" branding once real tenant branding is available; both use `BrandingSignalStore`
  for their displayed name/logo (add this binding to the templates explicitly).

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
ng test --include='**/login.component.spec.ts' --include='**/lock-screen.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1010 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1010 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1010` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `LoginComponent`/`LockScreenComponent` structures and the
`/login` route wiring, with confirmation both use `BrandingSignalStore` for display, never
hardcoded StoreOS branding.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1010: <one-line summary>"` only after verification is
   fully green.
