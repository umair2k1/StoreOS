# P-004 — NuGet package pinning + central package management

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-004** of the StoreOS Cloud build ("NuGet package pinning + central package management") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-001, P-002, P-003 complete. Solution builds; docker dev stack runs; API
  and (deferred) web Dockerfiles exist.
- No NuGet packages beyond the default `dotnet new` templates have been added yet.

## 3. CONTEXT BLOCK

Use **NuGet Central Package Management** so every project pins from one
place. Create `backend/Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.0" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
    <PackageVersion Include="Npgsql" Version="10.0.0" />
    <PackageVersion Include="MediatR" Version="12.4.1" />
    <PackageVersion Include="FluentValidation" Version="11.10.0" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="11.10.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageVersion Include="System.IdentityModel.Tokens.Jwt" Version="8.1.2" />
    <PackageVersion Include="Hangfire.AspNetCore" Version="1.8.14" />
    <PackageVersion Include="Hangfire.PostgreSql" Version="1.20.10" />
    <PackageVersion Include="QuestPDF" Version="2024.10.2" />
    <PackageVersion Include="Serilog.AspNetCore" Version="8.0.3" />
    <PackageVersion Include="Serilog.Sinks.Console" Version="6.0.0" />
    <PackageVersion Include="Serilog.Enrichers.CorrelationId" Version="3.0.1" />
    <PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.9.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.9.0" />
    <PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.9.0-beta.1" />
    <PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.9.0" />
    <PackageVersion Include="Polly" Version="8.4.2" />
    <PackageVersion Include="Polly.Extensions.Http" Version="3.0.0" />
    <PackageVersion Include="AWSSDK.S3" Version="3.7.408" />
    <PackageVersion Include="SixLabors.ImageSharp" Version="3.1.5" />
    <PackageVersion Include="Microsoft.FeatureManagement" Version="3.5.0" />
    <PackageVersion Include="Konscious.Security.Cryptography.Argon2" Version="1.3.1" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageVersion Include="FluentAssertions" Version="6.12.1" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="3.10.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageVersion Include="NSwag.MSBuild" Version="14.1.0" />
    <PackageVersion Include="AspNetCore.HealthChecks.NpgSql" Version="9.0.0" />
    <PackageVersion Include="Swashbuckle.AspNetCore" Version="6.9.0" />
  </ItemGroup>
</Project>
```

Create `backend/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>13.0</LangVersion>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

Package allowlist rule: **no NuGet package outside the list above may be added in any later
prompt** without it first being added here. If a later prompt needs something not listed,
that is a defect in this manifest, not license to freelance a new dependency.

## 4. TASK

> **REVISION NOTE (review v2):** two extra steps belong to this prompt: (A) create a LOCAL tool manifest so every clean checkout/CI runner has the EF CLI: `dotnet new tool-manifest` then `dotnet tool install dotnet-ef --version 10.0.0` (same version as the EF Core pin) and commit `.config/dotnet-tools.json`; from now on `dotnet ef` means the local tool (`dotnet tool restore` first). (B) `StoreOS.Infrastructure.csproj` uses ASP.NET Core types (`IHttpContextAccessor`, `HttpContext`, JWT bearer); a class library does not get them by default, so add `<ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>` to `StoreOS.Infrastructure.csproj` (this is not a NuGet package, so the allowlist is unaffected).

1. Create `backend/Directory.Packages.props` and `backend/Directory.Build.props`
   exactly as shown.
2. Add the specific `<PackageReference Include="..." />` (no `Version` attribute — central
   management supplies it) entries to each project that needs them right now:
   - `StoreOS.Api.csproj`: `Microsoft.EntityFrameworkCore.Design`, `Serilog.AspNetCore`,
     `Serilog.Sinks.Console`, `OpenTelemetry.Extensions.Hosting`,
     `OpenTelemetry.Instrumentation.AspNetCore`,
     `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
     `AspNetCore.HealthChecks.NpgSql`.
   - `StoreOS.Infrastructure.csproj`: `Microsoft.EntityFrameworkCore`,
     `Npgsql.EntityFrameworkCore.PostgreSQL`, `MediatR`, `Hangfire.AspNetCore`,
     `Hangfire.PostgreSql`, `QuestPDF`, `Polly`, `AWSSDK.S3`, `SixLabors.ImageSharp`,
     `Microsoft.FeatureManagement`, `Konscious.Security.Cryptography.Argon2`,
     `System.IdentityModel.Tokens.Jwt`, `Microsoft.AspNetCore.Authentication.JwtBearer`.
   - `StoreOS.Application.csproj`: `MediatR`, `FluentValidation`,
     `FluentValidation.DependencyInjectionExtensions`, `Microsoft.EntityFrameworkCore` (needed for `IAppDbContext`, `DbSet<T>`, `.Include`, `.AsNoTracking`; Application must NOT reference `StoreOS.Infrastructure`).
   - `StoreOS.Api.csproj` additionally: `Swashbuckle.AspNetCore` (used by P-1001 for the OpenAPI document; added here so the allowlist rule is not violated).
   - `StoreOS.Domain.Tests.csproj`, `StoreOS.IntegrationTests.csproj`,
     `StoreOS.ConcurrencyTests.csproj`: `xunit`, `xunit.runner.visualstudio`,
     `Microsoft.NET.Test.Sdk`, `FluentAssertions`.
   - `StoreOS.IntegrationTests.csproj` additionally: `Testcontainers.PostgreSql`,
     `Microsoft.AspNetCore.Mvc.Testing`.
3. Run `dotnet restore StoreOS.sln` from `backend/` (note: solution file is at repo root per
   P-001 — run `dotnet restore StoreOS.sln` from the repo root, or if you kept it under
   `backend/`, run it there; be consistent with wherever P-001 actually placed `StoreOS.sln`
   and record the exact path in your report).
4. Run `dotnet build StoreOS.sln` and confirm it still succeeds with the new package
   references (even though nothing uses them yet — unused `using` is fine, unused
   PackageReference is fine).

## 5. HARD CONSTRAINTS

- Every package version above is exact and MUST be used verbatim
  in every later prompt. Do not let any later step silently float to a newer version via
  `dotnet add package X` without a version — always rely on central management (no explicit
  version in individual `.csproj` files).
- Do not add `PackageReference` entries to projects that do not need them yet (e.g. do not
  add QuestPDF to `StoreOS.Domain` — it must remain 100% dependency-free per Plan §5.1).

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
dotnet restore StoreOS.sln
dotnet build StoreOS.sln
# Expected: Build succeeded, 0 Error(s).
dotnet list backend/src/StoreOS.Domain/StoreOS.Domain.csproj package
# Expected: (no PackageReference rows) — StoreOS.Domain must stay dependency-free.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-004 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-004 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-004` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete, final `Directory.Packages.props` package list (name+version)
and which project each was added to, since every later prompt must match these exact
versions.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-004: <one-line summary>"` only after verification is
   fully green.
