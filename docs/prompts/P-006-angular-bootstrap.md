# P-006 — Angular workspace bootstrap (PWA schematics)

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-006** of the StoreOS Cloud build ("Angular workspace bootstrap (PWA schematics)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Backend (P-001–005) is fully scaffolded, building, and has a CI skeleton.
- `frontend/` currently contains only `.gitkeep`. This prompt creates the real Angular
  workspace there. Do not touch `backend/` in this prompt.

## 3. CONTEXT BLOCK

Use **Angular 21** (`@angular/cli@21`) with standalone-components style,
routing, SCSS, and the PWA schematic. Exact commands, run from the repo root
(`store-os-cloud/`):

```bash
rm -f frontend/.gitkeep
npx -y @angular/cli@21 new storeos-web --directory=frontend --routing --style=scss \
  --standalone --skip-git --package-manager=npm
cd frontend
npx -y @angular/cli@21 add @angular/pwa --project=storeos-web --skip-confirmation
```

This produces `frontend/` populated with a standard Angular 21 standalone-app workspace
named `storeos-web`, plus PWA assets (`ngsw-config.json`, `manifest.webmanifest`,
`src/assets/icons/`, and service-worker registration wired into `app.config.ts`).

Confirm `frontend/angular.json` has `"projects": { "storeos-web": { ... } }` — this exact
project name is required because P-003's `Dockerfile.web` references
`dist/storeos-web/browser`.

## 4. TASK

1. Run the exact commands in the CONTEXT BLOCK from the repo root.
2. Confirm `frontend/package.json` `"name"` is `"storeos-web"` (or the workspace project
   name is `storeos-web` in `angular.json` even if the package name differs) — the build
   output path `dist/storeos-web/browser` used by `Dockerfile.web` (P-003) must exist after
   `ng build --configuration production`.
3. Confirm the PWA schematic added `provideServiceWorker('ngsw-worker.js', ...)` to
   `frontend/src/app/app.config.ts` and that `frontend/public/manifest.webmanifest` (or
   `src/manifest.webmanifest` depending on CLI version) exists — note the exact path found,
   since P-1008 replaces this static manifest with a dynamic one served by the backend and
   needs to know exactly where the `<link rel="manifest">` tag lives in `index.html`.
4. Run `npm run build -- --configuration production` inside `frontend/` and confirm the
   output lands at `frontend/dist/storeos-web/browser/index.html`.
5. Do not write any custom application code yet — this is scaffolding only. Do not modify
   generated component templates beyond what the CLI schematics produced.

## 5. HARD CONSTRAINTS

- Angular version: 21.x exactly (via `npx @angular/cli@21`).
- Project name inside `angular.json` must be exactly `storeos-web` — every later Dockerfile
  and build script path depends on this.
- Do not add any UI library (Material, PrimeNG, Tailwind, etc.) in this prompt — that
  decision, if needed, is deferred to the frontend-design skill guidance used in Phase 11
  feature prompts; keep this prompt to bare scaffolding.

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
npm ci
npm run build -- --configuration production
ls dist/storeos-web/browser/index.html
# Expected: file exists
cat angular.json | python3 -c "import json,sys; d=json.load(sys.stdin); print(list(d['projects'].keys()))"
# Expected: ['storeos-web']
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-006 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-006 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-006` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact Angular CLI version used, the confirmed project name
`storeos-web`, the exact path to `index.html`'s `<link rel="manifest">` tag, and the exact
build output directory `dist/storeos-web/browser`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-006: <one-line summary>"` only after verification is
   fully green.
