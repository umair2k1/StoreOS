# P-1001 — Angular core module: HTTP client, environment config, generated API client wiring (NSwag)

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1001** of the StoreOS Cloud build ("Angular core module: HTTP client, environment config, generated API client wiring (NSwag)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- The COMPLETE backend (Phases 0-9, P-001 through P-906) is built,
  tested, and green. Every REST endpoint referenced by any Phase 10/11 prompt already exists
  and is documented in docs/ARTIFACTS.md.
- The Angular workspace (P-006/007) exists as a bare scaffold with PWA schematics applied, but
  has NO application code yet - `frontend/src/app/` contains only the default `ng new`
  generated app component.

## 3. CONTEXT BLOCK

Install NSwag for OpenAPI-to-TypeScript client generation. In
`frontend/`:
```bash
npm install --save-exact @angular/common@18.2.13 --force # confirm exact version already present from ng new; skip if already matching
```
(Most Angular packages are already correctly versioned from P-006/007 - this step is a
no-op confirmation, not a fresh install, unless a version drift is found.)

Confirm the backend exposes an OpenAPI/Swagger document. Add to `StoreOS.Api.csproj` (if not
already present from earlier prompts - it likely is NOT yet, since no prompt explicitly added
Swagger generation):
```xml
<PackageReference Include="Swashbuckle.AspNetCore" />
```
Add to `Directory.Packages.props`: `<PackageVersion Include="Swashbuckle.AspNetCore" Version="6.9.0" />`.
Wire in `Program.cs`:
```csharp
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
// ... after app.Build():
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

Create `frontend/nswag.json` configuring TypeScript client generation from the running API's
OpenAPI document (`http://localhost:5000/swagger/v1/swagger.json`) into
`frontend/src/app/core/api/generated-client.ts`, using the `fetch`-based Angular template.

Create `backend/src/StoreOS.Api/generate-client.sh` (a small script running the API briefly,
fetching its OpenAPI doc, and invoking `nswag run ../../frontend/nswag.json`) - OR, simpler and
more reliable in an automated agent context: skip the "run the live API" step and instead have
NSwag generate directly from the compiled assembly via `nswag aspnetcore2openapi` pointed at the
built `StoreOS.Api.dll`, avoiding the need to manage a live running process during codegen.
Choose and implement whichever approach is more reliable in a headless CI-like environment
(prefer the assembly-based approach for determinism).

Create `frontend/src/app/core/api/api-config.ts` providing the generated client's base URL from
Angular environment files (`environment.ts`/`environment.production.ts`, create both with an
`apiBaseUrl` field, `http://localhost:5000/api` for dev).

## 4. TASK

1. Add Swashbuckle to the backend, wire Swagger/OpenAPI generation, confirm
   `dotnet run --project backend/src/StoreOS.Api` (or building + a headless doc-generation
   command) produces a valid OpenAPI JSON document.
2. Create `frontend/nswag.json` and the generation script, using the assembly-based
   (`aspnetcore2openapi`) approach for headless determinism.
3. Run the generation once to produce `frontend/src/app/core/api/generated-client.ts`, and
   confirm it compiles as part of `ng build`.
4. Create `frontend/src/environments/environment.ts` and `environment.production.ts` with
   `apiBaseUrl` fields.
5. Create `frontend/src/app/core/api/api-config.ts` wiring the generated client's constructor to
   read `apiBaseUrl` from the environment via Angular's `provideHttpClient()` + a custom
   `HTTP_INTERCEPTORS`-free base-URL injection (NSwag-generated clients typically accept a
   base-URL string in their constructor via a DI token - wire an `API_BASE_URL` injection token
   provided from the environment file).
6. Add an npm script `"generate-client"` to `package.json` invoking the regeneration command, and
   wire CI (`.github/workflows/ci.yml`) to run it and `git diff --exit-code` against the
   checked-in generated file, failing the build on drift (per Plan Section 11's "CI regenerates
   and fails on drift" requirement).
7. Confirm `ng build` succeeds with the generated client present and correctly typed.

## 5. HARD CONSTRAINTS

- The generated client (`generated-client.ts`) is the ONLY sanctioned way any
  Angular code in this codebase calls the backend API - no feature prompt in Phase 11 may write
  a raw `HttpClient.get(...)`/`post(...)` call bypassing the generated client, per Plan Section
  11's "No hand-written API calls" rule.
- CI must fail if the checked-in generated client has drifted from the live OpenAPI spec -
  wire this check now, even though the full CI integration (Testcontainers, etc.) is finished
  later in P-1203; this specific drift check can and should work today.

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
cd store-os-cloud
dotnet build StoreOS.sln
npm --prefix frontend run generate-client
cd frontend && ng build && cd ..
git diff --exit-code frontend/src/app/core/api/generated-client.ts
# Expected: build succeeds, generated client compiles, and is up to date (no diff).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1001 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1001 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1001` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the NSwag configuration approach (assembly-based generation), the
`API_BASE_URL` injection token, the environment file structure, and the CI drift-check command
now wired into `.github/workflows/ci.yml`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1001: <one-line summary>"` only after verification is
   fully green.
