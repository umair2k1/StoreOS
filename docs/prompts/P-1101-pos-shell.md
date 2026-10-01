# P-1101 — POS screen: search/scan/cart shell + hotkeys

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1101** of the StoreOS Cloud build ("POS screen: search/scan/cart shell + hotkeys") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 10 (Angular foundation, P-1001-1011) fully complete: auth, shell,
  branding, shared design system all work.
- No feature screens exist yet. This begins the largest remaining phase: the actual retail
  operating system UI. POS is the single most important screen in the product per Requirements
  Section 35 ("prioritize: 1. Fast billing... 2. Low typing effort").

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/pos/pos.component.ts` (standalone,
the main POS shell) with a search input auto-focused on load, wired to
`GET /api/pos/search?q=` (P-503, via the generated client) with debounced (300ms) reactive
search using RxJS (`toSignal` + `switchMap` per Plan Section 12.2's stated pattern):

```typescript
import { Component, ElementRef, ViewChild, AfterViewInit, signal, computed } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, switchMap, of } from 'rxjs';
import { PosApiClient } from '../../core/api/generated-client'; // exact class name depends on
                                                                   // NSwag's generated output -
                                                                   // confirm the actual name
                                                                   // from docs/ARTIFACTS.md's
                                                                   // P-1001 record before using it

@Component({
  selector: 'app-pos',
  standalone: true,
  imports: [ReactiveFormsModule /*, shared components */],
  templateUrl: './pos.component.html'
})
export class PosComponent implements AfterViewInit {
  @ViewChild('searchInput') searchInputRef!: ElementRef<HTMLInputElement>;
  searchControl = new FormControl('', { nonNullable: true });
  searchResults = toSignal(
    this.searchControl.valueChanges.pipe(
      debounceTime(300), distinctUntilChanged(),
      switchMap(q => q.trim().length > 0 ? this.posApi.search(q) : of([]))
    ), { initialValue: [] }
  );

  private lastKeyTime = 0;
  private scanBuffer = '';

  constructor(private posApi: PosApiClient) {}

  ngAfterViewInit(): void { this.searchInputRef.nativeElement.focus(); }

  onSearchKeydown(event: KeyboardEvent): void {
    const now = performance.now();
    const delta = now - this.lastKeyTime;
    this.lastKeyTime = now;
    if (delta < 50 && event.key !== 'Enter') {
      this.scanBuffer += event.key; // fast keystrokes -> likely a scanner, not human typing
    } else if (event.key === 'Enter' && this.scanBuffer.length > 3) {
      // Scanner-speed input ending in Enter: treat as a completed barcode/IMEI scan.
      this.searchControl.setValue(this.scanBuffer);
      this.scanBuffer = '';
    } else {
      this.scanBuffer = '';
    }
  }

  @HostListener('window:keydown', ['$event'])
  handleHotkeys(event: KeyboardEvent): void {
    if (event.key === 'F2') { event.preventDefault(); this.searchInputRef.nativeElement.focus(); }
    // '+'/'-' qty, 'Del' remove line, 'F4' payment, 'F8' park cart are wired in P-1103/1104
    // once the cart/payment sub-components exist to receive these commands.
  }
}
```

This precisely implements Requirements Section 6.2's scanner-vs-typing distinction: rapid
keystrokes (<50ms apart) followed by Enter are treated as a completed scan and trigger an
immediate lookup, distinguishing hardware scanner input from a human typing normally into the
search box.

## 4. TASK

1. Confirm the EXACT generated API client class/method names from
   `docs/ARTIFACTS.md`'s P-1001 record (NSwag's naming may differ from the illustrative
   `PosApiClient`/`.search(q)` shown above - adjust to match reality).
2. Create `PosComponent` (`.ts`+`.html`+`.scss`) in `frontend/src/app/features/pos/` exactly as
   described, implementing the debounced search AND the scanner-timing-based keydown handler.
3. Add loading/empty/error states (from P-1011's shared components) around the search results
   list.
4. Wire the `/pos` route.
5. Write `pos.component.spec.ts`: (a) typing into the search box with realistic (>50ms apart)
   keystroke timing does NOT trigger the scan-buffer path; (b) simulating rapid (<50ms apart)
   keystrokes followed by Enter DOES populate the search control with the buffered string and
   triggers a search (use `fakeAsync`/`tick` to control timing precisely in the test); (c) the
   search input is focused automatically on component init; (d) `F2` refocuses the search input
   even after focus has moved elsewhere.

## 5. HARD CONSTRAINTS

- The scanner-vs-typing distinction is based on REAL inter-keystroke timing
  (<50ms), matching Plan Section 12.4's exact stated threshold - do not simplify this to "any
  Enter press" or a fixed-length heuristic alone; timing is the core signal.
- Use the ACTUAL generated API client names/shapes from `docs/ARTIFACTS.md`, not the
  illustrative names shown in this prompt's context block.

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
ng test --include='**/pos.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1101 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1101 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1101` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `PosComponent` structure, the scanner-timing detection algorithm (50ms
threshold), and the hotkey list wired so far (F2; others deferred to P-1103/1104).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1101: <one-line summary>"` only after verification is
   fully green.
