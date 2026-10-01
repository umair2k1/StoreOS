# P-1006 — App shell: top nav, branch switcher, dynamic title/branding service

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1006** of the StoreOS Cloud build ("App shell: top nav, branch switcher, dynamic title/branding service") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1005 complete: auth guard and permission directive both work.
- No application shell (top nav, branch switcher, dynamic branding) exists yet - the app is
  still just the bare `ng new` default component.

## 3. CONTEXT BLOCK

Create `frontend/src/app/core/branding/branding-signal-store.ts`:

```typescript
import { Injectable, signal, computed } from '@angular/core';

export interface BrandingState {
  tenantName: string;
  activeBranchName: string | null;
  logoUrl: string | null;
  themeColor: string;
}

@Injectable({ providedIn: 'root' })
export class BrandingSignalStore {
  private readonly state = signal<BrandingState>({
    tenantName: 'StoreOS', // internal fallback ONLY before onboarding/login populates real data -
                            // never shown to a real logged-in user of a fully onboarded tenant
    activeBranchName: null, logoUrl: null, themeColor: '#1e293b'
  });
  readonly tenantName = computed(() => this.state().tenantName);
  readonly activeBranchName = computed(() => this.state().activeBranchName);
  readonly logoUrl = computed(() => this.state().logoUrl);

  setBranding(data: Partial<BrandingState>): void {
    this.state.update(s => ({ ...s, ...data }));
  }
}
```

Create `frontend/src/app/shared/shell/shell.component.ts` (standalone component, top nav +
branch switcher + router outlet):

```typescript
import { Component, effect, inject } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink],
  template: `
    <header class="shell-header">
      <span class="brand-name">{{ branding.tenantName() }}</span>
      @if (branding.activeBranchName(); as branchName) {
        <span class="branch-badge">{{ branchName }}</span>
      }
      <nav>
        <a routerLink="/pos">POS</a>
        <a routerLink="/sales">Sales</a>
        <a routerLink="/inventory">Inventory</a>
        <a routerLink="/customers">Customers</a>
        <a routerLink="/reports">Reports</a>
      </nav>
    </header>
    <main><router-outlet /></main>
  `,
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  branding = inject(BrandingSignalStore);
  private titleService = inject(Title);
  private auth = inject(AuthSignalStore);

  constructor() {
    effect(() => {
      const branchPart = this.branding.activeBranchName();
      const tenantPart = this.branding.tenantName();
      this.titleService.setTitle(branchPart ? `${branchPart} | ${tenantPart}` : tenantPart);
    });
  }
}
```

The branch SWITCHER itself (a dropdown to change `X-Branch-Id` for subsequent requests) is a
richer sub-component - create `frontend/src/app/shared/shell/branch-switcher.component.ts`
displayed only when the user has multiple accessible branches (`authStore.branchIds().length >
1 || authStore.allBranches()`), backed by a small `BranchContextService` that stores the
CURRENTLY SELECTED branch id (separate signal, defaulting to the user's default branch from
login) and provides it to an HTTP interceptor that attaches `X-Branch-Id` to outgoing requests -
create this interceptor too: `frontend/src/app/core/interceptors/branch-context.interceptor.ts`.

## 4. TASK

1. Create `BrandingSignalStore` exactly as shown.
2. Create `frontend/src/app/core/branch/branch-context.service.ts`: a small signal-based
   service holding `currentBranchId` (settable, defaults from login response's default branch or
   first accessible branch), exposing it for both the switcher UI and the interceptor.
3. Create `frontend/src/app/core/interceptors/branch-context.interceptor.ts` attaching
   `X-Branch-Id` (from `BranchContextService.currentBranchId()`) to every outgoing request when
   set, and register it in `app.config.ts`.
4. Create `ShellComponent` and `BranchSwitcherComponent` exactly as described, with the
   dynamic-title `effect()`.
5. Wire `ShellComponent` as the root layout in `app.routes.ts` (a parent route with child
   routes for each feature area, guarded by `authGuard`).
6. Write `shell.component.spec.ts`: the document title updates correctly when
   `BrandingSignalStore`'s branch/tenant name signals change, without a manual re-render trigger
   (proving the `effect()` reactivity). Write `branch-switcher.component.spec.ts`: the switcher
   is hidden for a single-branch user and shown for a multi-branch user; selecting a different
   branch updates `BranchContextService.currentBranchId()`.

## 5. HARD CONSTRAINTS

- `BrandingSignalStore`'s default fallback tenant name is `'StoreOS'` ONLY as a
  pre-login/pre-onboarding placeholder - once a real tenant's branding loads (via login response
  or the onboarding flow, P-1007), this must NEVER be shown again to a real user, per the
  product's strict white-labeling requirement (Requirements Section 3.5). Do not let `'StoreOS'`
  leak into any user-facing surface once branding data is available.

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
ng test --include='**/shell.component.spec.ts' --include='**/branch-switcher.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1006 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1006 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1006` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `BrandingSignalStore`/`BranchContextService` signatures, the
`ShellComponent`/`BranchSwitcherComponent`, the `branch-context.interceptor.ts`, and the
white-labeling constraint (never leak 'StoreOS' post-login).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1006: <one-line summary>"` only after verification is
   fully green.
