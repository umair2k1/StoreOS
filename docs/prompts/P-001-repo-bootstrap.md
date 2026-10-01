# P-001 — Repo bootstrap, solution skeleton, docs ledger

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-001** of the StoreOS Cloud build ("Repo bootstrap, solution skeleton, docs ledger") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- This is the very first prompt. The repository does not exist yet.
- You are starting from an empty working directory.
- Nothing else has been built. There is no "do not modify" list yet.

## 3. CONTEXT BLOCK

Target repository layout (create every directory shown, even if a directory is
initially just a `.gitkeep` placeholder):

```text
store-os-cloud/
├── backend/
│   ├── src/
│   │   ├── StoreOS.Api/
│   │   ├── StoreOS.Domain/
│   │   ├── StoreOS.Application/
│   │   ├── StoreOS.Infrastructure/
│   │   └── StoreOS.Modules/
│   └── tests/
│       ├── StoreOS.Domain.Tests/
│       ├── StoreOS.IntegrationTests/
│       └── StoreOS.ConcurrencyTests/
├── frontend/                (leave empty with .gitkeep; created in P-006)
├── deploy/                  (leave empty with .gitkeep; created in P-002/P-003)
├── docs/
└── tools/
    ├── pz-migrate/
    └── seed/
```

Use **.NET 10 LTS** for every backend project (`dotnet --version` must report a 10.x SDK;
if not installed, install the .NET 10 SDK first via your platform's standard method before
proceeding).

Exact commands to scaffold the solution and projects:

```bash
mkdir -p store-os-cloud && cd store-os-cloud
dotnet new sln -n StoreOS
mkdir -p backend/src backend/tests frontend deploy docs tools/pz-migrate tools/seed

dotnet new classlib -n StoreOS.Domain -o backend/src/StoreOS.Domain -f net10.0
dotnet new classlib -n StoreOS.Application -o backend/src/StoreOS.Application -f net10.0
dotnet new classlib -n StoreOS.Infrastructure -o backend/src/StoreOS.Infrastructure -f net10.0
dotnet new classlib -n StoreOS.Modules -o backend/src/StoreOS.Modules -f net10.0
dotnet new webapi -n StoreOS.Api -o backend/src/StoreOS.Api -f net10.0 --use-controllers

dotnet new xunit -n StoreOS.Domain.Tests -o backend/tests/StoreOS.Domain.Tests -f net10.0
dotnet new xunit -n StoreOS.IntegrationTests -o backend/tests/StoreOS.IntegrationTests -f net10.0
dotnet new xunit -n StoreOS.ConcurrencyTests -o backend/tests/StoreOS.ConcurrencyTests -f net10.0

dotnet sln StoreOS.sln add backend/src/StoreOS.Domain/StoreOS.Domain.csproj
dotnet sln StoreOS.sln add backend/src/StoreOS.Application/StoreOS.Application.csproj
dotnet sln StoreOS.sln add backend/src/StoreOS.Infrastructure/StoreOS.Infrastructure.csproj
dotnet sln StoreOS.sln add backend/src/StoreOS.Modules/StoreOS.Modules.csproj
dotnet sln StoreOS.sln add backend/src/StoreOS.Api/StoreOS.Api.csproj
dotnet sln StoreOS.sln add backend/tests/StoreOS.Domain.Tests/StoreOS.Domain.Tests.csproj
dotnet sln StoreOS.sln add backend/tests/StoreOS.IntegrationTests/StoreOS.IntegrationTests.csproj
dotnet sln StoreOS.sln add backend/tests/StoreOS.ConcurrencyTests/StoreOS.ConcurrencyTests.csproj

dotnet add backend/src/StoreOS.Application reference backend/src/StoreOS.Domain
dotnet add backend/src/StoreOS.Infrastructure reference backend/src/StoreOS.Application
dotnet add backend/src/StoreOS.Infrastructure reference backend/src/StoreOS.Domain
dotnet add backend/src/StoreOS.Modules reference backend/src/StoreOS.Application
dotnet add backend/src/StoreOS.Modules reference backend/src/StoreOS.Infrastructure
dotnet add backend/src/StoreOS.Api reference backend/src/StoreOS.Modules
dotnet add backend/src/StoreOS.Api reference backend/src/StoreOS.Application
dotnet add backend/src/StoreOS.Api reference backend/src/StoreOS.Infrastructure
dotnet add backend/src/StoreOS.Api reference backend/src/StoreOS.Domain

dotnet add backend/tests/StoreOS.Domain.Tests reference backend/src/StoreOS.Domain
dotnet add backend/tests/StoreOS.IntegrationTests reference backend/src/StoreOS.Api
dotnet add backend/tests/StoreOS.ConcurrencyTests reference backend/src/StoreOS.Api
```

Module boundary rule (Plan §5.1): mark internal types in each module `internal` where they
are implementation detail, and add an `InternalsVisibleTo` entry in each project's `.csproj`
for its corresponding test project, e.g. in `StoreOS.Domain.csproj`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="StoreOS.Domain.Tests" />
</ItemGroup>
```

## 4. TASK

1. Run every command in the CONTEXT BLOCK exactly as given, from a fresh empty
   directory, to produce the `store-os-cloud/` repository root.
2. Create `store-os-cloud/.gitignore` with standard .NET + Node + IDE entries: `bin/`,
   `obj/`, `node_modules/`, `.vs/`, `.vscode/` (except `.vscode/extensions.json`), `*.user`,
   `dist/`, `.angular/`, `*.log`, `.env`.
3. Create `store-os-cloud/docs/BUILD_LOG.md` with this exact header and nothing else below it
   yet:
   ```markdown
   # StoreOS Cloud — Build Log

   Append-only. One line per completed prompt: `<PROMPT_ID> | <STATUS> | <FILES>`.
   If a prompt is blocked, insert a line starting with `BLOCKED:` as the FIRST line below
   the `---` separator (never above the `# StoreOS Cloud — Build Log` title). Remove the
   BLOCKED line only when the prompt is later completed.

   ---
   ```
4. Create `store-os-cloud/docs/ARTIFACTS.md` with this exact header:
   ```markdown
   # StoreOS Cloud — Artifact Registry

   This file is the single source of truth for every interface, entity, DB table/column,
   REST endpoint, and Angular component/service signature created so far. Every later
   prompt's CONTEXT BLOCK must be checked against this file. Sections are appended in
   prompt-completion order and are never rewritten, only appended to.

   ---
   ```
5. Add `<InternalsVisibleTo>` entries as shown for `StoreOS.Domain` → `StoreOS.Domain.Tests`,
   `StoreOS.Application` → `StoreOS.IntegrationTests`, `StoreOS.Infrastructure` →
   `StoreOS.IntegrationTests`, `StoreOS.Api` → `StoreOS.IntegrationTests` and
   `StoreOS.ConcurrencyTests`.
6. Confirm the full directory tree matches the CONTEXT BLOCK layout, including empty
   `frontend/.gitkeep`, `deploy/.gitkeep`, `tools/pz-migrate/.gitkeep`, `tools/seed/.gitkeep`.
7. Initialize git: `git init`, then `git add -A`.

## 5. HARD CONSTRAINTS

- .NET SDK version: 10.0.x only. Do not use preview SDKs.
- Do not add any NuGet package in this prompt beyond what `dotnet new` scaffolds by default.
  Package pinning is handled in P-004.
- Do not write any business logic yet — this prompt is scaffolding only.

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
# Expected: Build succeeded. 0 Warning(s) relevant to missing references. 0 Error(s).
git status --porcelain | wc -l
# Expected: non-zero (files staged), confirming git init + add worked.
```
Grep for forbidden patterns (should return nothing, this prompt has no logic yet):
```bash
grep -rn "TODO\|FIXME\|NotImplementedException" backend/ || echo "clean"
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-001 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-001 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-001` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact solution file name (`StoreOS.sln`), the full project list with
their `.csproj` paths, and the exact `docs/BUILD_LOG.md` / `docs/ARTIFACTS.md` header
contents so later prompts append correctly.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-001: <one-line summary>"` only after verification is
   fully green.
