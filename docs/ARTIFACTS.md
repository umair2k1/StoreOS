# StoreOS Cloud — Artifact Registry

This file is the single source of truth for every interface, entity, DB table/column,
REST endpoint, and Angular component/service signature created so far. Every later
prompt's CONTEXT BLOCK must be checked against this file. Sections are appended in
prompt-completion order and are never rewritten, only appended to.

---

## P-001

### Solution
- `StoreOS.sln` (at repository root)

### Projects
- `backend/src/StoreOS.Domain/StoreOS.Domain.csproj` (`net10.0`, classlib)
  - InternalsVisibleTo: `StoreOS.Domain.Tests`
- `backend/src/StoreOS.Application/StoreOS.Application.csproj` (`net10.0`, classlib)
  - Reference: `StoreOS.Domain`
  - InternalsVisibleTo: `StoreOS.IntegrationTests`
- `backend/src/StoreOS.Infrastructure/StoreOS.Infrastructure.csproj` (`net10.0`, classlib)
  - References: `StoreOS.Application`, `StoreOS.Domain`
  - InternalsVisibleTo: `StoreOS.IntegrationTests`
- `backend/src/StoreOS.Modules/StoreOS.Modules.csproj` (`net10.0`, classlib)
  - References: `StoreOS.Application`, `StoreOS.Infrastructure`
- `backend/src/StoreOS.Api/StoreOS.Api.csproj` (`net10.0`, webapi with controllers)
  - References: `StoreOS.Modules`, `StoreOS.Application`, `StoreOS.Infrastructure`, `StoreOS.Domain`
  - InternalsVisibleTo: `StoreOS.IntegrationTests`, `StoreOS.ConcurrencyTests`
- `backend/tests/StoreOS.Domain.Tests/StoreOS.Domain.Tests.csproj` (`net10.0`, xunit)
  - Reference: `StoreOS.Domain`
- `backend/tests/StoreOS.IntegrationTests/StoreOS.IntegrationTests.csproj` (`net10.0`, xunit)
  - Reference: `StoreOS.Api`
- `backend/tests/StoreOS.ConcurrencyTests/StoreOS.ConcurrencyTests.csproj` (`net10.0`, xunit)
  - Reference: `StoreOS.Api`

### Placeholders & Structure
- `frontend/.gitkeep`
- `deploy/.gitkeep`
- `tools/pz-migrate/.gitkeep`
- `tools/seed/.gitkeep`
- `.gitignore`

### Headers & Conventions Established
- `docs/BUILD_LOG.md` header:
  ```markdown
  # StoreOS Cloud — Build Log

  Append-only. One line per completed prompt: `<PROMPT_ID> | <STATUS> | <FILES>`.
  If a prompt is blocked, insert a line starting with `BLOCKED:` as the FIRST line below
  the `---` separator (never above the `# StoreOS Cloud — Build Log` title). Remove the
  BLOCKED line only when the prompt is later completed.

  ---
  ```
- `docs/ARTIFACTS.md` header:
  ```markdown
  # StoreOS Cloud — Artifact Registry

  This file is the single source of truth for every interface, entity, DB table/column,
  REST endpoint, and Angular component/service signature created so far. Every later
  prompt's CONTEXT BLOCK must be checked against this file. Sections are appended in
  prompt-completion order and are never rewritten, only appended to.

  ---
  ```

## P-002

### Services & Containers
- `postgres`:
  - Container name: `storeos-postgres`
  - Image: `postgres:16-alpine`
  - Port: `5432:5432`
  - Database: `storeos`
  - Owner user/password: `storeos` / `storeos_dev_password`
  - App role user/password: `storeos_app` / `storeos_app_dev_password` (`NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`)
  - Volume: `storeos_pg_data` -> `/var/lib/postgresql/data`
  - Init scripts: `deploy/db/init/01-app-role.sh` -> `/docker-entrypoint-initdb.d:ro`
- `minio`:
  - Container name: `storeos-minio`
  - Image: `minio/minio:latest`
  - Ports: `9000:9000` (API/S3), `9001:9001` (Console)
  - Root user/password: `storeos_minio` / `storeos_minio_password`
  - Volume: `storeos_minio_data` -> `/data`
- `otel-collector`:
  - Container name: `storeos-otel-collector`
  - Image: `otel/opentelemetry-collector-contrib:0.96.0`
  - Ports: `4317:4317` (OTLP gRPC), `4318:4318` (OTLP HTTP)
  - Config: `deploy/otel-collector-config.yaml` -> `/etc/otel-collector-config.yaml`
- `api`:
  - Container name: `storeos-api`
  - Profile: `full`
  - Build context: `../backend`, dockerfile: `../deploy/Dockerfile.api`
  - Ports: `5000:8080`

### Database Scripts & Schemas
- `deploy/db/init/01-app-role.sh`: Provisions `storeos_app` role with `NOSUPERUSER NOBYPASSRLS`.
- `deploy/db/post-migrate.sql`: Post-migration script revoking `UPDATE, DELETE, TRUNCATE` on append-only tables from `storeos_app` (`audit_log`, `customer_ledger_entries`, `supplier_ledger_entries`, `inventory_movements`, `repair_job_status_history`, `customer_payments`, `liability_payments`, and `__EFMigrationsHistory`).
- `deploy/db/apply-post-migrate.sh`: Utility script to run `post-migrate.sql` against `$STOREOS_MIGRATIONS_CONNECTION_URI`.

## P-003

### Dockerfiles & Configurations
- `deploy/Dockerfile.api`:
  - Multi-stage build (.NET 10 SDK -> ASP.NET Core 10 runtime).
  - Non-root user: `storeos` (UID 1000, GID 1000).
  - Exposes port `8080`, sets `ASPNETCORE_URLS=http://+:8080`, `ASPNETCORE_ENVIRONMENT=Production`.
  - Build context in `deploy/docker-compose.yml`: `..` (repo root), copying `backend/` and publishing `src/StoreOS.Api/StoreOS.Api.csproj`.
- `deploy/Dockerfile.web`:
  - Multi-stage build (`node:24-alpine` -> `nginx:1.27-alpine`).
  - Non-root user: `storeos` (UID 1000, GID 1000).
  - Serves compiled SPA from `/src/dist/storeos-web/browser` via custom non-root Nginx config.
  - Build verification note: Build is DEFERRED until P-007 (after Angular workspace and `frontend/package.json` are initialized in P-006).
- `deploy/nginx.conf`:
  - Nginx configuration listening on port `8080`.
  - SPA fallback: `try_files $uri $uri/ /index.html`.
  - Static asset caching: 30d immutable cache headers for common static extensions.

### Compose Service Updates (`deploy/docker-compose.yml`)
- `api`:
  - Build context: `..`
  - Dockerfile: `deploy/Dockerfile.api`
  - Container name: `storeos-api`
  - Profile: `full`
  - Ports: `5000:8080`
- `web`:
  - Build context: `..`
  - Dockerfile: `deploy/Dockerfile.web`
  - Container name: `storeos-web`
  - Profile: `full`
  - Ports: `8081:8080`

## P-004

### Tool Manifest & CLI Tools
- `.config/dotnet-tools.json`: Local .NET tool manifest with `dotnet-ef` pinned to version `10.0.0`.

### Build & Package Management Configurations
- `backend/Directory.Build.props`:
  - `TargetFramework`: `net10.0`
  - `Nullable`: `enable`
  - `ImplicitUsings`: `enable`
  - `LangVersion`: `13.0`
  - `TreatWarningsAsErrors`: `false`
  - `NoWarn`: `CS1591`
- `backend/Directory.Packages.props`:
  - `ManagePackageVersionsCentrally`: `true`
  - Centrally pinned packages (34 items):
    - `Microsoft.EntityFrameworkCore`: `10.0.0`
    - `Microsoft.EntityFrameworkCore.Design`: `10.0.0`
    - `Npgsql.EntityFrameworkCore.PostgreSQL`: `10.0.0`
    - `Npgsql`: `10.0.0`
    - `MediatR`: `12.4.1`
    - `FluentValidation`: `11.10.0`
    - `FluentValidation.DependencyInjectionExtensions`: `11.10.0`
    - `Microsoft.AspNetCore.Authentication.JwtBearer`: `10.0.0`
    - `System.IdentityModel.Tokens.Jwt`: `8.1.2`
    - `Hangfire.AspNetCore`: `1.8.14`
    - `Hangfire.PostgreSql`: `1.20.10`
    - `QuestPDF`: `2024.10.2`
    - `Serilog.AspNetCore`: `8.0.3`
    - `Serilog.Sinks.Console`: `6.0.0`
    - `Serilog.Enrichers.CorrelationId`: `3.0.1`
    - `OpenTelemetry.Extensions.Hosting`: `1.9.0`
    - `OpenTelemetry.Instrumentation.AspNetCore`: `1.9.0`
    - `OpenTelemetry.Instrumentation.EntityFrameworkCore`: `1.9.0-beta.1`
    - `OpenTelemetry.Exporter.OpenTelemetryProtocol`: `1.9.0`
    - `Polly`: `8.4.2`
    - `Polly.Extensions.Http`: `3.0.0`
    - `AWSSDK.S3`: `3.7.408`
    - `SixLabors.ImageSharp`: `3.1.5`
    - `Microsoft.FeatureManagement`: `3.5.0`
    - `Konscious.Security.Cryptography.Argon2`: `1.3.1`
    - `xunit`: `2.9.2`
    - `xunit.runner.visualstudio`: `2.8.2`
    - `Microsoft.NET.Test.Sdk`: `17.11.1`
    - `FluentAssertions`: `6.12.1`
    - `Testcontainers.PostgreSql`: `3.10.0`
    - `Microsoft.AspNetCore.Mvc.Testing`: `10.0.0`
    - `NSwag.MSBuild`: `14.1.0`
    - `AspNetCore.HealthChecks.NpgSql`: `9.0.0`
    - `Swashbuckle.AspNetCore`: `6.9.0`

### Project PackageReference Mappings
- `StoreOS.Api`:
  - `Microsoft.EntityFrameworkCore.Design`
  - `Serilog.AspNetCore`
  - `Serilog.Sinks.Console`
  - `OpenTelemetry.Extensions.Hosting`
  - `OpenTelemetry.Instrumentation.AspNetCore`
  - `OpenTelemetry.Exporter.OpenTelemetryProtocol`
  - `AspNetCore.HealthChecks.NpgSql`
  - `Swashbuckle.AspNetCore`
- `StoreOS.Infrastructure`:
  - FrameworkReference: `Microsoft.AspNetCore.App`
  - `Microsoft.EntityFrameworkCore`
  - `Npgsql.EntityFrameworkCore.PostgreSQL`
  - `MediatR`
  - `Hangfire.AspNetCore`
  - `Hangfire.PostgreSql`
  - `QuestPDF`
  - `Polly`
  - `AWSSDK.S3`
  - `SixLabors.ImageSharp`
  - `Microsoft.FeatureManagement`
  - `Konscious.Security.Cryptography.Argon2`
  - `System.IdentityModel.Tokens.Jwt`
  - `Microsoft.AspNetCore.Authentication.JwtBearer`
- `StoreOS.Application`:
  - `MediatR`
  - `FluentValidation`
  - `FluentValidation.DependencyInjectionExtensions`
  - `Microsoft.EntityFrameworkCore`
- `StoreOS.Domain`:
  - Dependency-free (0 package references).
- `StoreOS.Modules`:
  - No external package references.
- `StoreOS.Domain.Tests`:
  - `xunit`
  - `xunit.runner.visualstudio`
  - `Microsoft.NET.Test.Sdk`
  - `FluentAssertions`
- `StoreOS.IntegrationTests`:
  - `xunit`
  - `xunit.runner.visualstudio`
  - `Microsoft.NET.Test.Sdk`
  - `FluentAssertions`
  - `Testcontainers.PostgreSql`
  - `Microsoft.AspNetCore.Mvc.Testing`
- `StoreOS.ConcurrencyTests`:
  - `xunit`
  - `xunit.runner.visualstudio`
  - `Microsoft.NET.Test.Sdk`
  - `FluentAssertions`

## P-005

### CI Workflow
- File: `.github/workflows/ci.yml`
- Triggers:
  - `push` on all branches (`**`)
  - `pull_request` on all branches (`**`)
- Jobs:
  - `backend-build-test`:
    - Runs on: `ubuntu-latest`
    - Steps:
      - `actions/checkout@v4`
      - `actions/setup-dotnet@v4` (`dotnet-version: "10.0.x"`)
      - `dotnet restore StoreOS.sln`
      - `dotnet build StoreOS.sln --no-restore -c Release`
      - `dotnet test backend/tests/StoreOS.Domain.Tests --no-build -c Release --logger "trx;LogFileName=domain-tests.trx"`
      - `actions/upload-artifact@v4` (`name: domain-test-results`, `path: "**/domain-tests.trx"`, `if: always()`)
  - `frontend-build`:
    - Runs on: `ubuntu-latest`
    - Status: Gated with `if: false` in P-005; enabled in P-007 (runs unconditionally on push and PR)
    - Steps:
      - `actions/checkout@v4`
      - `actions/setup-node@v4` (`node-version: "24"`)
      - `npm ci --prefix frontend`
      - `npm run build --prefix frontend -- --configuration production`

## P-006

### Angular Workspace Bootstrap
- Angular CLI Version: `@angular/cli@21.2.24` (Angular 21.2.0 framework packages)
- Workspace Project Name: `storeos-web` (in `frontend/angular.json` and `frontend/package.json`)
- Project Type: Application (Standalone components, SCSS styling, Routing enabled)
- PWA Schematic: `@angular/pwa@21.2.24`
  - Service Worker Registration: `provideServiceWorker('ngsw-worker.js', { enabled: !isDevMode(), registrationStrategy: 'registerWhenStable:30000' })` in `frontend/src/app/app.config.ts`
  - Service Worker Configuration: `frontend/ngsw-config.json`
  - Web Manifest File: `frontend/public/manifest.webmanifest`
  - Manifest Link Tag: `<link rel="manifest" href="manifest.webmanifest" />` in `frontend/src/index.html` (line 9)
- Production Build Output Directory: `frontend/dist/storeos-web/browser` (entry: `index.html`)

### Components
- `App` (`frontend/src/app/app.ts`):
  - Decorator:
    ```typescript
    @Component({
      selector: 'app-root',
      imports: [RouterOutlet],
      templateUrl: './app.html',
      styleUrl: './app.scss'
    })
    ```
  - Class Signature:
    ```typescript
    export class App {
      protected readonly title = signal('storeos-web');
    }
    ```

## P-007

### Pinned Dependencies & Engines
- `rxjs`: `7.8.1` (exact pinned dependency in `frontend/package.json`)
- `zone.js`: `0.15.0` (exact pinned dependency in `frontend/package.json`, bumped from `0.14.10` to satisfy `@angular/core@21.2.25` peer dependency requirement `~0.15.0 || ~0.16.0`)
- `@playwright/test`: `1.48.2` (exact pinned devDependency in `frontend/package.json`)
- `engines` in `frontend/package.json`:
  ```json
  "engines": {
    "node": ">=24.0.0",
    "npm": ">=10.0.0"
  }
  ```

### Playwright Configuration
- Configuration File: `frontend/playwright.config.ts`
- Test Directory: `testDir: './e2e'`
- Target Directory: `frontend/e2e` (with placeholder `frontend/e2e/.gitkeep`)

### CI Workflow Updates
- File: `.github/workflows/ci.yml`
- Job: `frontend-build` enabled (removed `if: false`, runs unconditionally on `push` and `pull_request`)

### Docker Image Verification
- Image: `storeos-web:dev`
- Command: `docker build -f deploy/Dockerfile.web -t storeos-web:dev .` (verified build succeeds with output `/src/dist/storeos-web/browser`)

## P-101

### Interfaces
- `StoreOS.Application.Abstractions.IAppDbContext` (`backend/src/StoreOS.Application/Abstractions/IAppDbContext.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IAppDbContext
  {
      DbSet<TEntity> Set<TEntity>() where TEntity : class;
      DatabaseFacade Database { get; }
      ChangeTracker ChangeTracker { get; }
      Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
  }
  ```

### DbContext & Factory
- `StoreOS.Infrastructure.Persistence.StoreOsDbContext` (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`):
  ```csharp
  namespace StoreOS.Infrastructure.Persistence;

  public class StoreOsDbContext : DbContext, StoreOS.Application.Abstractions.IAppDbContext
  {
      public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options) : base(options) { }
      protected override void OnModelCreating(ModelBuilder modelBuilder);
  }
  ```
- `StoreOS.Infrastructure.Persistence.StoreOsDbContextFactory` (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContextFactory.cs`):
  ```csharp
  namespace StoreOS.Infrastructure.Persistence;

  public sealed class StoreOsDbContextFactory : IDesignTimeDbContextFactory<StoreOsDbContext>
  {
      public StoreOsDbContext CreateDbContext(string[] args);
  }
  ```

### Connection Strings & Configuration
- Configuration File: `backend/src/StoreOS.Api/appsettings.json`
- Connection Strings:
  - `Default`: `"Host=localhost;Port=5432;Database=storeos;Username=storeos_app;Password=storeos_app_dev_password;Maximum Pool Size=50"`
  - `Reporting`: `"Host=localhost;Port=5432;Database=storeos;Username=storeos_app;Password=storeos_app_dev_password;Maximum Pool Size=20"`
- Design-time Migrations Connection: `STOREOS_MIGRATIONS_CONNECTION` env var or `"Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password"`

### CORS Policy
- Name: `"Default"`
- Allowed Origins: `http://localhost:4200`
- Headers & Methods: Any (`AllowAnyHeader()`, `AllowAnyMethod()`)
- Credentials: Enabled (`AllowCredentials()`)

### Health Check Endpoints
- `/health/live`: Liveness check (always returns HTTP 200 OK `Healthy` without evaluating dependencies)
- `/health/ready`: Readiness check (verifies PostgreSQL database connectivity via `AspNetCore.HealthChecks.NpgSql`, tagged `"ready"`)

### Documentation & Runbooks
- `docs/RUNBOOK-db-roles.md`: Architecture of PostgreSQL roles (`storeos` / `storeos_owner` and `storeos_app`), post-migration script requirements, and local reproduction procedures.

## P-102

### Domain Entities & Utilities
- `StoreOS.Domain.Tenancy.Slug` (`backend/src/StoreOS.Domain/Tenancy/Slug.cs`):
  ```csharp
  namespace StoreOS.Domain.Tenancy;
  public static class Slug
  {
      public static string FromName(string name);
  }
  ```
- `StoreOS.Domain.Tenancy.Tenant` (`backend/src/StoreOS.Domain/Tenancy/Tenant.cs`):
  ```csharp
  namespace StoreOS.Domain.Tenancy;
  public class Tenant
  {
      public Guid Id { get; }
      public string Name { get; }
      public string Subdomain { get; }
      public bool IsActive { get; }
      public string Timezone { get; }
      public string FeaturesJson { get; }
      public DateTimeOffset CreatedAt { get; }
      public static Tenant Create(string name, string timezone = "Asia/Kolkata", string? subdomain = null);
      public void Rename(string name);
      public void SetSubdomain(string subdomain);
      public void Deactivate();
      public void Activate();
      public void SetFeatures(string featuresJson);
  }
  ```
- `StoreOS.Domain.Tenancy.Branch` (`backend/src/StoreOS.Domain/Tenancy/Branch.cs`):
  ```csharp
  namespace StoreOS.Domain.Tenancy;
  public class Branch
  {
      public Guid Id { get; }
      public Guid TenantId { get; }
      public string Code { get; }
      public string Name { get; }
      public string? Address { get; }
      public string? Phone { get; }
      public string? Email { get; }
      public string? Gstin { get; }
      public bool IsActive { get; }
      public DateTimeOffset CreatedAt { get; }
      public static Branch Create(Guid tenantId, string code, string name);
      public void UpdateProfile(string name, string? address, string? phone, string? email, string? gstin);
      public void Deactivate();
      public void Activate();
  }
  ```

### Application Abstractions
- `StoreOS.Application.Abstractions.TenantPublicProfile` & `ITenantResolver` (`backend/src/StoreOS.Application/Abstractions/ITenantResolver.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;
  public record TenantPublicProfile(Guid TenantId, string Name, string Subdomain, bool IsActive);
  public interface ITenantResolver
  {
      Task<TenantPublicProfile?> ResolveFromHostAsync(string host, CancellationToken ct = default);
      Task<TenantPublicProfile?> ResolveByIdAsync(Guid tenantId, CancellationToken ct = default);
  }
  ```

### Infrastructure Services & DbContext Updates
- `StoreOS.Infrastructure.Tenancy.TenantResolver` (`backend/src/StoreOS.Infrastructure/Tenancy/TenantResolver.cs`):
  ```csharp
  namespace StoreOS.Infrastructure.Tenancy;
  public class TenantResolver : ITenantResolver
  {
      public TenantResolver(StoreOsDbContext dbContext, IConfiguration configuration);
      public Task<TenantPublicProfile?> ResolveFromHostAsync(string host, CancellationToken ct = default);
      public Task<TenantPublicProfile?> ResolveByIdAsync(Guid tenantId, CancellationToken ct = default);
  }
  ```
- `StoreOsDbContext` DbSet properties (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`):
  ```csharp
  public DbSet<Tenant> Tenants => Set<Tenant>();
  public DbSet<Branch> Branches => Set<Branch>();
  ```

### Database Schema & Migrations
- Migration: `001_InitTenancyBranches` (`backend/src/StoreOS.Infrastructure/Migrations/20261002134844_001_InitTenancyBranches.cs`)
  - Generation Command: `dotnet ef migrations add 001_InitTenancyBranches --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261002134844_001_InitTenancyBranches`
- Tables:
  - `public.tenants`:
    - `id` uuid PRIMARY KEY
    - `name` text NOT NULL
    - `subdomain` text NOT NULL UNIQUE (`ux_tenants_subdomain`), CHECK (`subdomain ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$'`)
    - `is_active` boolean NOT NULL DEFAULT true
    - `timezone` text NOT NULL DEFAULT 'Asia/Kolkata'
    - `features` jsonb NOT NULL DEFAULT '{}'
    - `created_at` timestamptz NOT NULL DEFAULT now()
  - `public.branches`:
    - `id` uuid PRIMARY KEY
    - `tenant_id` uuid NOT NULL REFERENCES `tenants(id)` ON DELETE RESTRICT
    - `code` text NOT NULL CHECK (`code ~ '^[A-Z1-9][A-Z0-9]{0,2}$'`)
    - `name` text NOT NULL
    - `address` text NULL
    - `phone` text NULL
    - `email` text NULL
    - `gstin` text NULL
    - `is_active` boolean NOT NULL DEFAULT true
    - `created_at` timestamptz NOT NULL DEFAULT now()
    - UNIQUE INDEX `IX_branches_tenant_id_code` ON (`tenant_id`, `code`)

## P-103

### Domain Entities
- `StoreOS.Domain.Tenancy.Setting` (`backend/src/StoreOS.Domain/Tenancy/Setting.cs`):
  ```csharp
  namespace StoreOS.Domain.Tenancy;
  public class Setting
  {
      public Guid TenantId { get; }
      public Guid? BranchId { get; }
      public string Key { get; }
      public string ValueJson { get; }
      public static Setting Create(Guid tenantId, Guid? branchId, string key, string valueJson);
      public void UpdateValue(string valueJson);
  }
  ```

### DbContext Updates
- `StoreOsDbContext` DbSet property (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`):
  ```csharp
  public DbSet<Setting> Settings => Set<Setting>();
  ```

### Canonical Setting Keys
- `"shop_profile"` (branch-level: name/address/phone/email/GSTIN/logo url/receipt footer)
- `"invoice_series_format"` (branch-level: `"FY_CONSECUTIVE"` or `"DAILY_TIMESTAMP"`)
- `"default_gst_rate"` (tenant-level, e.g. `18`)
- `"bank_config"` (tenant-level)
- `"finance_providers"` (tenant-level, JSON array)
- `"repair_module_enabled"` (tenant-level bool, mirrors `tenants.features.repairs` — reserved key)

### Database Schema & Migrations
- Migration: `002_Settings` (`backend/src/StoreOS.Infrastructure/Migrations/20261002135423_002_Settings.cs`)
  - Generation Command: `dotnet ef migrations add 002_Settings --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261002135423_002_Settings`
- Tables:
  - `public.settings`:
    - `tenant_id` uuid NOT NULL
    - `branch_id` uuid NULL REFERENCES `branches(id)` ON DELETE RESTRICT
    - `key` text NOT NULL
    - `value` jsonb NOT NULL
    - `branch_id_key` uuid NOT NULL GENERATED ALWAYS AS (`COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid)`) STORED
    - PRIMARY KEY (`tenant_id`, `branch_id_key`, `key`)
    - INDEX `IX_settings_branch_id` ON (`branch_id`)

## P-104

### Domain Entities
- `StoreOS.Domain.Identity.User` (`backend/src/StoreOS.Domain/Identity/User.cs`):
  ```csharp
  namespace StoreOS.Domain.Identity;
  public class User
  {
      public Guid Id { get; }
      public Guid TenantId { get; }
      public string Email { get; }
      public string PasswordHash { get; }
      public string DisplayName { get; }
      public int TokenVersion { get; }
      public bool IsActive { get; }
      public DateTimeOffset CreatedAt { get; }
      public static User Create(Guid tenantId, string email, string passwordHash, string displayName);
      public void ChangePasswordHash(string newHash);
      public void BumpTokenVersion();
      public void Deactivate();
      public void Activate();
  }
  ```
- `StoreOS.Domain.Identity.Role` (`backend/src/StoreOS.Domain/Identity/Role.cs`):
  ```csharp
  namespace StoreOS.Domain.Identity;
  public class Role
  {
      public Guid Id { get; }
      public Guid TenantId { get; }
      public string Name { get; }
      public static Role Create(Guid tenantId, string name);
      public void Rename(string name);
  }
  ```
- `StoreOS.Domain.Identity.RolePermission` (`backend/src/StoreOS.Domain/Identity/RolePermission.cs`):
  ```csharp
  namespace StoreOS.Domain.Identity;
  public class RolePermission
  {
      public Guid RoleId { get; }
      public string PermissionCode { get; }
      public static RolePermission Create(Guid roleId, string permissionCode);
  }
  ```
- `StoreOS.Domain.Identity.UserBranch` (`backend/src/StoreOS.Domain/Identity/UserBranch.cs`):
  ```csharp
  namespace StoreOS.Domain.Identity;
  public class UserBranch
  {
      public Guid UserId { get; }
      public Guid BranchId { get; }
      public bool IsDefault { get; }
      public static UserBranch Create(Guid userId, Guid branchId, bool isDefault = false);
      public void SetDefault(bool isDefault);
  }
  ```
- `StoreOS.Domain.Identity.UserRoleAssignment` (`backend/src/StoreOS.Domain/Identity/UserRoleAssignment.cs`):
  ```csharp
  namespace StoreOS.Domain.Identity;
  public class UserRoleAssignment
  {
      public Guid UserId { get; }
      public Guid RoleId { get; }
      public Guid? BranchId { get; }
      public static UserRoleAssignment Create(Guid userId, Guid roleId, Guid? branchId = null);
  }
  ```

### DbContext Updates
- `StoreOsDbContext` DbSet properties (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`):
  ```csharp
  public DbSet<User> Users => Set<User>();
  public DbSet<Role> Roles => Set<Role>();
  public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
  public DbSet<UserBranch> UserBranches => Set<UserBranch>();
  public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
  ```

### Database Schema & Migrations
- Migration: `003_Identity` (`backend/src/StoreOS.Infrastructure/Migrations/20261002135950_003_Identity.cs`)
  - Generation Command: `dotnet ef migrations add 003_Identity --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261002135950_003_Identity`
- Tables:
  - `public.users`:
    - `id` uuid PRIMARY KEY
    - `tenant_id` uuid NOT NULL
    - `email` text NOT NULL
    - `password_hash` text NOT NULL
    - `display_name` text NOT NULL
    - `token_version` integer NOT NULL DEFAULT 1
    - `is_active` boolean NOT NULL DEFAULT true
    - `created_at` timestamptz NOT NULL DEFAULT now()
    - UNIQUE INDEX `IX_users_tenant_id_email` ON (`tenant_id`, `email`)
  - `public.roles`:
    - `id` uuid PRIMARY KEY
    - `tenant_id` uuid NOT NULL
    - `name` text NOT NULL
  - `public.role_permissions`:
    - `role_id` uuid NOT NULL REFERENCES `roles(id)` ON DELETE CASCADE
    - `permission_code` text NOT NULL
    - PRIMARY KEY (`role_id`, `permission_code`)
  - `public.user_branches`:
    - `user_id` uuid NOT NULL REFERENCES `users(id)` ON DELETE CASCADE
    - `branch_id` uuid NOT NULL REFERENCES `branches(id)` ON DELETE RESTRICT
    - `is_default` boolean NOT NULL DEFAULT false
    - PRIMARY KEY (`user_id`, `branch_id`)
    - INDEX `IX_user_branches_branch_id` ON (`branch_id`)
  - `public.user_role_assignments`:
    - `user_id` uuid NOT NULL REFERENCES `users(id)` ON DELETE CASCADE
    - `role_id` uuid NOT NULL REFERENCES `roles(id)` ON DELETE RESTRICT
    - `branch_id` uuid NULL REFERENCES `branches(id)` ON DELETE RESTRICT
    - `branch_id_key` uuid NOT NULL GENERATED ALWAYS AS (`COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid)`) STORED
    - PRIMARY KEY (`user_id`, `role_id`, `branch_id_key`)
    - INDEX `IX_user_role_assignments_branch_id` ON (`branch_id`)
    - INDEX `IX_user_role_assignments_role_id` ON (`role_id`)

## P-105

### Domain Entities
- `StoreOS.Domain.Identity.RefreshToken` (`backend/src/StoreOS.Domain/Identity/RefreshToken.cs`):
  ```csharp
  namespace StoreOS.Domain.Identity;

  public class RefreshToken
  {
      public Guid Id { get; private set; }
      public Guid UserId { get; private set; }
      public string TokenHash { get; private set; } = default!;
      public Guid FamilyId { get; private set; }
      public bool IsRevoked { get; private set; }
      public Guid? ReplacedByTokenId { get; private set; }
      public DateTimeOffset ExpiresAt { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }

      private RefreshToken() { }

      public static RefreshToken IssueNew(Guid userId, string tokenHash, Guid familyId, DateTimeOffset expiresAt);
      public void RotateTo(Guid newTokenId);
      public void Revoke();
      public bool IsExpired(DateTimeOffset now);
  }
  ```

### DbContext Updates
- `StoreOsDbContext` DbSet properties (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`):
  ```csharp
  public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
  ```

### Database Schema & Migrations
- Migration: `004_RefreshTokens` (`backend/src/StoreOS.Infrastructure/Migrations/20261005024540_004_RefreshTokens.cs`)
  - Generation Command: `dotnet ef migrations add 004_RefreshTokens --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261005024540_004_RefreshTokens`
- Tables:
  - `public.refresh_tokens`:
    - `id` uuid PRIMARY KEY
    - `user_id` uuid NOT NULL REFERENCES `users(id)`
    - `token_hash` text NOT NULL
    - `family_id` uuid NOT NULL
    - `is_revoked` boolean NOT NULL DEFAULT false
    - `replaced_by_token_id` uuid NULL REFERENCES `refresh_tokens(id)`
    - `expires_at` timestamptz NOT NULL
    - `created_at` timestamptz NOT NULL DEFAULT now()
    - INDEX `ix_refresh_tokens_family` ON (`family_id`)
    - INDEX `ix_refresh_tokens_lookup` ON (`user_id`, `token_hash`)
    - INDEX `IX_refresh_tokens_replaced_by_token_id` ON (`replaced_by_token_id`)

## P-106

### Application Abstractions
- `StoreOS.Application.Abstractions.IAccessScope` (`backend/src/StoreOS.Application/Abstractions/IAccessScope.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IAccessScope
  {
      Guid TenantId { get; }
      Guid UserId { get; }
      IReadOnlySet<Guid> BranchIds { get; }
      bool AllBranches { get; }
      IReadOnlySet<string> Permissions { get; }
      int TokenVersion { get; }
      bool IsAuthenticated { get; }
      bool HasTenantContext { get; }
      void UseAnonymousTenant(Guid tenantId);
      void UseSystemContext(Guid tenantId);
      bool HasBranchAccess(Guid branchId);
      bool HasPermission(string permissionCode);
  }
  ```
- `StoreOS.Application.Abstractions.IAnonymousTenantRequest` (`backend/src/StoreOS.Application/Abstractions/IAnonymousTenantRequest.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IAnonymousTenantRequest
  {
      Guid TenantId { get; }
  }
  ```

### Infrastructure Implementations
- `StoreOS.Infrastructure.Security.HttpAccessScope` (`backend/src/StoreOS.Infrastructure/Security/HttpAccessScope.cs`):
  Scoped implementation extracting claims from `IHttpContextAccessor.HttpContext.User`.
- `StoreOS.Infrastructure.Persistence.NullAccessScope` (`backend/src/StoreOS.Infrastructure/Persistence/NullAccessScope.cs`):
  Sealed implementation for design-time factory and schema-only tests with empty collections and throwing context switchers.

### JWT Claim Types Convention
Fixed claim types consumed by `HttpAccessScope` and produced by token issuance:
- `sub`: user ID (`Guid`)
- `tenant_id`: tenant ID (`Guid`)
- `all_branches`: `"true"` or `"false"`
- `branch_id`: repeated claim, one per accessible branch (omitted when `all_branches="true"`)
- `perm`: repeated claim, one per granted permission code
- `token_version`: integer user token version string

### Integration Test Auth Injection Pattern
- `TestAuthHandler` (`backend/tests/StoreOS.IntegrationTests/Security/TestAuthHandler.cs`):
  AuthenticationHandler registered under scheme `"TestScheme"`, reading `AsyncLocal<ClaimsPrincipal?> TestAuthHandler.CurrentPrincipal`.
- In scoped tests with `WebApplicationFactory`:
  Set `scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext { User = principal }` prior to resolving `IAccessScope`.

## P-107

### DbContext Constructor Update
- `StoreOsDbContext` constructor (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`):
  ```csharp
  public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options, IAccessScope scope) : base(options)
  {
      _scope = scope;
  }
  ```
- Design-time factory update (`backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContextFactory.cs`):
  ```csharp
  return new StoreOsDbContext(options, new NullAccessScope());
  ```

### Query Filters Applied
- `Branch`: `modelBuilder.Entity<Branch>().HasQueryFilter(b => b.TenantId == _scope.TenantId);`
- `Setting`: `modelBuilder.Entity<Setting>().HasQueryFilter(s => s.TenantId == _scope.TenantId);`

## Query Filter Convention

```text
For every new entity E added in any later prompt:
- If E has ONLY TenantId (no BranchId): add
  modelBuilder.Entity<E>().HasQueryFilter(x => x.TenantId == _scope.TenantId);
- If E has BOTH TenantId and BranchId (operational/branch-scoped data): add
  modelBuilder.Entity<E>().HasQueryFilter(x =>
      x.TenantId == _scope.TenantId &&
      (_scope.AllBranches || _scope.BranchIds.Contains(x.BranchId)));
- If E has a NULLABLE BranchId representing "head office / tenant-wide" (e.g. expenses,
  supplier_transactions): add
  modelBuilder.Entity<E>().HasQueryFilter(x =>
      x.TenantId == _scope.TenantId &&
      (x.BranchId == null || _scope.AllBranches || _scope.BranchIds.Contains(x.BranchId.Value)));
- These EF filters are LAYER ONE of defense. Layer TWO is PostgreSQL RLS (P-108/P-110).
  Both must independently enforce the same isolation — never rely on only one.
```

## P-108

### Infrastructure Services
- `StoreOS.Infrastructure.Persistence.RlsRoleGuard` (`backend/src/StoreOS.Infrastructure/Persistence/RlsRoleGuard.cs`):
  `IHostedService` executing `SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user` on startup. Throws `InvalidOperationException` if either is true.

### Shared Test Fixture
- `StoreOS.IntegrationTests.PostgresTestFixture` (`backend/tests/StoreOS.IntegrationTests/PostgresTestFixture.cs`):
  Shared container test fixture with `OwnerConnectionString` (superuser/owner) and `AppConnectionString` (role `storeos_app` with `NOSUPERUSER NOBYPASSRLS`).
- `[CollectionDefinition("postgres")] public class PostgresCollection : ICollectionFixture<PostgresTestFixture>`

### Database Schema & Migrations
- Migration: `005_RlsTenantOnlyPolicies` (`backend/src/StoreOS.Infrastructure/Migrations/20261005031649_005_RlsTenantOnlyPolicies.cs`)
  - Generation Command: `dotnet ef migrations add 005_RlsTenantOnlyPolicies --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261005031649_005_RlsTenantOnlyPolicies`

### Row-Level Security Policies (8 Protected Tables)
All 8 tables have `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY`:
1. Direct `tenant_id` tables (`branches`, `users`, `roles`, `settings`):
   ```sql
   CREATE POLICY tenant_isolation_policy ON <table> USING (
       tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
   );
   ```
2. Subquery on `roles` (`role_permissions`):
   ```sql
   CREATE POLICY tenant_isolation_policy ON role_permissions USING (
       role_id IN (SELECT id FROM roles WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
   );
   ```
3. Subquery on `users` (`user_branches`, `user_role_assignments`, `refresh_tokens`):
   ```sql
   CREATE POLICY tenant_isolation_policy ON <table> USING (
       user_id IN (SELECT id FROM users WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
   );
   ```

### RLS Exception Documentation
- `public.tenants` is the **sole intentional exception** to the 100% RLS coverage rule. It has no parent tenant scope; it serves as the lookup table for tenant resolution and is queried before any `IAccessScope` or session context exists.

## P-112

### Application Abstractions
- `StoreOS.Application.Abstractions.ICorrelationContextAccessor` (`backend/src/StoreOS.Application/Abstractions/ICorrelationContextAccessor.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface ICorrelationContextAccessor
  {
      string CorrelationId { get; set; }
  }
  ```

### Infrastructure Implementations
- `StoreOS.Infrastructure.Observability.CorrelationContextAccessor` (`backend/src/StoreOS.Infrastructure/Observability/CorrelationContextAccessor.cs`):
  Scoped implementation holding `CorrelationId` property.

### HTTP Middleware & Pipeline Position
- `StoreOS.Api.Middleware.CorrelationIdMiddleware` (`backend/src/StoreOS.Api/Middleware/CorrelationIdMiddleware.cs`):
  - Header Name Constant: `public const string HeaderName = "X-Correlation-ID";`
  - Format: GUID string in `"D"` format (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`).
  - Pipeline Position: **FIRST** HTTP middleware immediately following `var app = builder.Build();` and before any other `app.Use...` call (`app.UseMiddleware<CorrelationIdMiddleware>();`).
  - Enriches Serilog `LogContext.PushProperty("CorrelationId", correlationId)`.

### Serilog Host Configuration
- Wired in `backend/src/StoreOS.Api/Program.cs` via `builder.Host.UseSerilog(...)` with console output template including `CorrelationId={CorrelationId}`.

## P-113

### Domain Exceptions
- `StoreOS.Domain.Exceptions.DomainException` (`backend/src/StoreOS.Domain/Exceptions/DomainException.cs`):
  ```csharp
  namespace StoreOS.Domain.Exceptions;

  public class DomainException : Exception
  {
      public string Code { get; }
      public DomainException(string code, string message) : base(message) => Code = code;
  }
  ```
- `StoreOS.Domain.Exceptions.DomainValidationException` (`backend/src/StoreOS.Domain/Exceptions/DomainException.cs`):
  ```csharp
  namespace StoreOS.Domain.Exceptions;

  public class DomainValidationException : DomainException
  {
      public DomainValidationException(string code, string message) : base(code, message) { }
  }
  ```

### API Error Catalog & HTTP Middleware
- `StoreOS.Api.Middleware.ErrorCatalog` (`backend/src/StoreOS.Api/Middleware/ErrorCatalog.cs`):
  Central mapping of error code strings to `HttpStatusCode`.
  - Explicit mappings: 401 (`unauthenticated`, `invalid_credentials`, `token_version_stale`, `invalid_refresh_token`, `refresh_token_expired`, `refresh_token_reuse_detected`, `tenant_required`), 403 (`forbidden`, `tenant_inactive`), 400 (`branch_context_required`, `idempotency_key_required`, `idempotency_key_invalid`), 409 (`branch_code_taken`, `imei_already_active`, `idempotency_key_reuse`, `idempotency_in_progress`, `serialization_conflict`).
  - Wildcard mapping: codes ending with `_not_found` map to 404 (`HttpStatusCode.NotFound`).
  - Default fallback: 422 (`HttpStatusCode.UnprocessableEntity`).
- `StoreOS.Api.Middleware.ExceptionHandlingMiddleware` (`backend/src/StoreOS.Api/Middleware/ExceptionHandlingMiddleware.cs`):
  - Pipeline Position: **SECOND** HTTP middleware immediately following `CorrelationIdMiddleware` and before all other middleware (`app.UseMiddleware<ExceptionHandlingMiddleware>();`).
  - Formats errors as RFC 7807 `ProblemDetails` with `application/problem+json` content type.
  - Exact `ProblemDetails` extension key names:
    - `correlationId` (string, GUID in "D" format)
    - `code` (string error code)
    - `errors` (dictionary of string field name to string array of messages, present on validation errors)
  - Adds `Retry-After: 1` header on `serialization_conflict` errors.

## P-109

### Application Abstractions
- `StoreOS.Application.Abstractions.IRequestMarkers` (`backend/src/StoreOS.Application/Abstractions/IRequestMarkers.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IReadOnlyRequest { }
  public interface IReportingRequest { }
  public interface ISerializableRequest { }
  ```
- `StoreOS.Application.Abstractions.ITenantJobRunner` (`backend/src/StoreOS.Application/Abstractions/ITenantJobRunner.cs`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface ITenantJobRunner
  {
      Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct = default);
  }
  ```

### Infrastructure Implementations
- `StoreOS.Infrastructure.Persistence.RlsSessionInterceptor` (`backend/src/StoreOS.Infrastructure/Persistence/RlsSessionInterceptor.cs`):
  - Inherits `DbTransactionInterceptor`.
  - Intercepts `TransactionStartedAsync` to execute:
    `SELECT set_config('app.tenant_id', @t, true), set_config('app.branch_ids', @b, true), set_config('app.all_branches', @a, true)`
  - Registered as scoped and added to `StoreOsDbContext` interceptors.
- `StoreOS.Infrastructure.Persistence.TransactionBehavior<TRequest, TResponse>` (`backend/src/StoreOS.Infrastructure/Persistence/TransactionBehavior.cs`):
  - MediatR `IPipelineBehavior<TRequest, TResponse>` handling `IAnonymousTenantRequest` and managing single database transaction per request.
  - Executes `SET LOCAL transaction_read_only = on` for `IReadOnlyRequest`.
- `StoreOS.Infrastructure.Persistence.TenantJobRunner` (`backend/src/StoreOS.Infrastructure/Persistence/TenantJobRunner.cs`):
  - Scoped implementation creating an async scope, setting `UseSystemContext(tenantId)`, running work in a `ReadCommitted` transaction, committing on success, rolling back on failure.

### Isolation Level Mapping Table
| Request marker | DbContext | Isolation Level | Mode | Retry on 40001/40P01 (P-511) |
|---|---|---|---|---|
| `IReadOnlyRequest` | `StoreOsDbContext` | ReadCommitted | READ ONLY | no |
| `IReportingRequest` | `ReportingDbContext` (P-119) | RepeatableRead | READ ONLY | no |
| `ISerializableRequest` | `StoreOsDbContext` | Serializable | read-write | yes |
| Any other request | `StoreOsDbContext` | RepeatableRead | read-write | yes |

### MediatR Pipeline Behavior Order (DECISIONS D-04, P-114)
1. `ValidationBehavior` (validation, 400)
2. `SerializationRetryBehavior` (retries transaction on 40001/40P01, P-511)
3. `TransactionBehavior` (opens transaction, RLS session variables, commits/rollbacks, P-109)
4. `TokenVersionBehavior` (token revocation check, P-307)
5. `AuthorizationBehavior` (permissions and high impact checks, P-308)
6. `IdempotencyBehavior` (idempotency key inside transaction, P-117)
7. Handler

### HTTP Middleware Order (Program.cs)
`CorrelationId` -> `ExceptionHandling` -> `Authentication` -> `AccessScope` -> `BranchContext` -> `Authorization` -> controllers

## P-110

### Regression Fixture Table
- Table `public.rls_probe_branch_scoped` (permanent regression fixture):
  - `id uuid PRIMARY KEY`
  - `tenant_id uuid NOT NULL`
  - `branch_id uuid NOT NULL`
  - `label text NOT NULL`
  - `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY`
  - Policy `tenant_branch_isolation_policy`

### Database Schema & Migrations
- Migration: `006_RlsBranchScopedPolicies` (`backend/src/StoreOS.Infrastructure/Migrations/20261005035212_006_RlsBranchScopedPolicies.cs`)
  - Generation Command: `dotnet ef migrations add 006_RlsBranchScopedPolicies --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261005035212_006_RlsBranchScopedPolicies`

### Empty Branch-List Sentinel Convention
When `_scope.BranchIds` is empty and `_scope.AllBranches` is false, `RlsSessionInterceptor` emits the nil-GUID sentinel:
`00000000-0000-0000-0000-000000000000`
into `app.branch_ids` instead of an empty string, preventing PostgreSQL syntax/type-cast errors on `string_to_array(...)` while matching zero real branches.

## RLS Archetype B (copy verbatim for every branch-scoped table)

Every future branch-scoped operational entity (`sales`, `branch_inventory`, `purchases`, `inventory_imeis`, `inventory_movements`, `customer_payments`, `customer_ledger_entries`, `supplier_ledger_entries`, `expenses`, `repair_jobs`, etc.) copies this exact `USING` clause verbatim, substituting only the table name:

```sql
ALTER TABLE <table> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <table> FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_branch_isolation_policy ON <table> USING (
  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
  AND (
    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
  )
);
```

## P-111

### CI RLS Coverage Tests
- Class: `StoreOS.IntegrationTests.Schema.RlsCoverageTests` (`backend/tests/StoreOS.IntegrationTests/Schema/RlsCoverageTests.cs`)
  - `[Collection("postgres")]`
  - Reuses `PostgresTestFixture` (`backend/tests/StoreOS.IntegrationTests/PostgresTestFixture.cs`) and `NullAccessScope` (`backend/src/StoreOS.Infrastructure/Persistence/NullAccessScope.cs`)
  - Test methods:
    - `Every_table_with_tenant_id_column_has_rls_enabled_and_forced()`
    - `No_policy_contains_forbidden_null_bypass()`
    - `Every_table_with_tenant_id_has_at_least_one_policy()`
    - `Every_policy_using_current_setting_contains_nullif()`
    - `Consecutive_transactions_on_single_connection_do_not_leak_tenant_context()`

### CI Integration Stage Requirement
- Reminder: P-1203 MUST include `RlsCoverageTests` (`dotnet test --filter RlsCoverageTests`) in the CI integration-test stage with Testcontainers / Docker support.

## P-114

### MediatR Pipeline Behaviors & Markers
- Marker Assembly Class: `StoreOS.Application.AssemblyMarker` (`backend/src/StoreOS.Application/AssemblyMarker.cs`)
  ```csharp
  public sealed class AssemblyMarker { private AssemblyMarker() { } }
  ```
- Marker Interface: `StoreOS.Application.Abstractions.IRequiresPermission` (`backend/src/StoreOS.Application/Abstractions/IRequiresPermission.cs`)
  ```csharp
  public interface IRequiresPermission
  {
      string RequiredPermission { get; }
  }
  ```
- Validation Pipeline Behavior: `StoreOS.Application.Behaviors.ValidationBehavior<TRequest, TResponse>` (`backend/src/StoreOS.Application/Behaviors/ValidationBehavior.cs`)
  ```csharp
  public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
  ```
- Authorization Pipeline Behavior: `StoreOS.Application.Behaviors.AuthorizationBehavior<TRequest, TResponse>` (`backend/src/StoreOS.Application/Behaviors/AuthorizationBehavior.cs`)
  ```csharp
  public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
  ```

### MediatR Behavior Pipeline Registration Order (Program.cs / CONTRACTS §3 / DECISIONS D-04)
1. `ValidationBehavior` (pure validation, 400, outermost)
2. `SerializationRetryBehavior` (retries whole transaction on 40001/40P01, P-511)
3. `TransactionBehavior` (opens transaction, RLS session variables, commits/rollbacks, P-109)
4. `TokenVersionBehavior` (token version revocation check, P-307)
5. `AuthorizationBehavior` (claims permission check + live DB check, P-114 / P-308)
6. `IdempotencyBehavior` (idempotency key inside transaction, P-117)
7. Handler

### Status Code Mapping Table (ErrorCatalog / ExceptionHandlingMiddleware)
| Code | HTTP Status | Description |
|---|---|---|
| `unauthenticated` | 401 Unauthorized | Missing or invalid authentication |
| `forbidden` | 403 Forbidden | Authenticated principal lacks required permission |
| Any other DomainException | 422 Unprocessable Entity | Domain/business rule violation (or 404 for `*_not_found`, 409 for conflicts) |
| `ValidationException` | 400 Bad Request | Field-level validation error dictionary |

## P-115

### Permission Catalog
- Class: `StoreOS.Domain.Identity.PermissionCatalog` (`backend/src/StoreOS.Domain/Identity/PermissionCatalog.cs`)
- Constants (All 17 canonical permission codes):
  - `PosCheckout` = `"pos:checkout"`
  - `SalesDiscountApply` = `"sales:discount:apply"`
  - `SalesVoid` = `"sales:void"`
  - `SalesRefund` = `"sales:refund"`
  - `SalesCreditOverride` = `"sales:credit:override"`
  - `SalesReadAnyBranch` = `"sales:read:any-branch"`
  - `InventoryCostView` = `"inventory:cost:view"`
  - `ReportsProfitView` = `"reports:profit:view"`
  - `InventoryManage` = `"inventory:manage"`
  - `PurchasesManage` = `"purchases:manage"`
  - `InventoryAdjustments` = `"inventory:adjustments"`
  - `ExpensesManage` = `"expenses:manage"`
  - `LiabilitiesManage` = `"liabilities:manage"`
  - `RepairsManage` = `"repairs:manage"`
  - `SettingsManage` = `"settings:manage"`
  - `UsersManage` = `"users:manage"`
  - `BranchesManage` = `"branches:manage"`

### Permission Checker
- Interface: `StoreOS.Application.Identity.IPermissionChecker` (`backend/src/StoreOS.Application/Identity/IPermissionChecker.cs`)
  ```csharp
  public interface IPermissionChecker
  {
      bool IsValidPermissionCode(string code);
      IReadOnlyList<string> AllCodes { get; }
  }
  ```
- Implementation: `StoreOS.Infrastructure.Identity.PermissionChecker` (`backend/src/StoreOS.Infrastructure/Identity/PermissionChecker.cs`)
  ```csharp
  public class PermissionChecker : IPermissionChecker
  ```
- DI Registration: `builder.Services.AddSingleton<IPermissionChecker, PermissionChecker>();`

## P-116

### Audit Log Entity & Writer
- Entity: `StoreOS.Domain.Platform.AuditLogEntry` (`backend/src/StoreOS.Domain/Platform/AuditLogEntry.cs`)
  ```csharp
  public class AuditLogEntry
  {
      public long Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid? BranchId { get; private set; }
      public string? CorrelationId { get; private set; }
      public DateTimeOffset OccurredAt { get; private set; }
      public Guid? UserId { get; private set; }
      public string Action { get; private set; }
      public string EntityType { get; private set; }
      public string EntityId { get; private set; }
      public string? ChangesJson { get; private set; }
      public static AuditLogEntry Create(Guid tenantId, Guid? branchId, string? correlationId, Guid? userId, string action, string entityType, string entityId, string? changesJson);
  }
  ```
- Writer Interface: `StoreOS.Application.Platform.IAuditWriter` (`backend/src/StoreOS.Application/Platform/IAuditWriter.cs`)
  ```csharp
  public interface IAuditWriter
  {
      Task WriteAsync(Guid? branchId, string action, string entityType, string entityId, object? changes, CancellationToken ct = default);
  }
  ```
  *Note: `WriteAsync` only stages the entry (`AddAsync`) to the ambient `StoreOsDbContext`; it does NOT call `SaveChangesAsync()` itself.*
- Writer Implementation: `StoreOS.Infrastructure.Platform.AuditWriter` (`backend/src/StoreOS.Infrastructure/Platform/AuditWriter.cs`)
  ```csharp
  public class AuditWriter : IAuditWriter
  ```
- DI Registration: `builder.Services.AddScoped<IAuditWriter, AuditWriter>();`

### Database Migrations
- Migration: `007_AuditLog` (`backend/src/StoreOS.Infrastructure/Migrations/20261005044244_007_AuditLog.cs`)
  - Generation Command: `dotnet ef migrations add 007_AuditLog --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261005044244_007_AuditLog`
  - Table: `public.audit_log` with `GENERATED ALWAYS AS IDENTITY PRIMARY KEY`, partial index `ix_audit_log_tenant_correlation`, RLS enabled and forced, policy `tenant_branch_isolation_policy` supporting nullable `branch_id`.

## P-117

### In-Transaction Idempotency Contracts & Behavior
- Marker Interface: `StoreOS.Application.Abstractions.IIdempotentRequest` (`backend/src/StoreOS.Application/Abstractions/IIdempotentRequest.cs`)
  ```csharp
  public interface IIdempotentRequest
  {
      string? IdempotencyKey { get; }
      string IdempotencyScope { get; }
      bool IdempotencyRequired { get; }
  }
  ```
- Store Interface: `StoreOS.Application.Abstractions.IIdempotencyStore` (`backend/src/StoreOS.Application/Abstractions/IIdempotentRequest.cs`)
  ```csharp
  public enum IdempotencyOutcome { Started, Replay, Mismatch, InProgress }
  public sealed record IdempotencyBegin(IdempotencyOutcome Outcome, string? ResponseJson);
  public interface IIdempotencyStore
  {
      Task<IdempotencyBegin> BeginAsync(string key, string scope, string requestHash, CancellationToken ct);
      Task CompleteAsync(string key, string scope, string responseJson, CancellationToken ct);
  }
  ```
- Pipeline Behavior: `StoreOS.Application.Behaviors.IdempotencyBehavior<TRequest, TResponse>` (`backend/src/StoreOS.Application/Behaviors/IdempotencyBehavior.cs`)
  ```csharp
  public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
  ```
- Replay Flag: `StoreOS.Application.Behaviors.IdempotencyReplayFlag` (`backend/src/StoreOS.Application/Behaviors/IdempotencyBehavior.cs`)
  ```csharp
  public sealed class IdempotencyReplayFlag { public bool Replayed { get; set; } }
  ```
- Store Implementation: `StoreOS.Infrastructure.Persistence.IdempotencyStore` (`backend/src/StoreOS.Infrastructure/Persistence/IdempotencyStore.cs`)
  ```csharp
  public class IdempotencyStore : IIdempotencyStore
  ```

### DDL & RLS Policies
```sql
CREATE TABLE idempotency_keys (
  tenant_id  uuid NOT NULL,
  user_id    uuid NOT NULL,
  key        text NOT NULL,
  endpoint   text NOT NULL,
  request_hash text NOT NULL,
  response_status int,
  response_body  jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, user_id, endpoint, key)
);
ALTER TABLE idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE idempotency_keys FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_policy ON idempotency_keys
  USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
CREATE INDEX ix_idempotency_created ON idempotency_keys (created_at);
```

### Database Migrations
- Migration: `008_Idempotency` (`backend/src/StoreOS.Infrastructure/Migrations/20261005050329_008_Idempotency.cs`)
  - Generation Command: `dotnet ef migrations add 008_Idempotency --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Database Update Command: `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Migration HEAD: `20261005050329_008_Idempotency`

### MediatR Behavior Order (DECISIONS D-04, CONTRACTS §3)
`ValidationBehavior` -> `SerializationRetryBehavior` (P-511) -> `TransactionBehavior` -> `TokenVersionBehavior` (P-307) -> `AuthorizationBehavior` -> `IdempotencyBehavior` -> handler

## P-118

### Package Allowlist Amendments (Directory.Packages.props)
- Added package version:
  ```xml
  <PackageVersion Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" Version="3.3.4" />
  ```
  *Note: Analyzer-only reference restricted strictly to `StoreOS.Domain.csproj` via `<PackageReference Include="Microsoft.CodeAnalysis.BannedApiAnalyzers" PrivateAssets="all" />` and `<AdditionalFiles Include="BannedSymbols.txt" />`.*

### Money Utilities
- Class: `StoreOS.Domain.Common.Money` (`backend/src/StoreOS.Domain/Common/Money.cs`)
  ```csharp
  public static class Money
  {
      public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
  }
  ```

### Banned Symbols Configuration
- File: `backend/src/StoreOS.Domain/BannedSymbols.txt`
  ```text
  M:System.Math.Pow(System.Double,System.Double);Use decimal-based DecimalPow instead of Math.Pow for financial calculations (see FinanceEngine).
  T:System.Double;Do not use double for monetary values; use decimal.
  T:System.Single;Do not use float for monetary values; use decimal.
  ```
- Analyzer Diagnostics: RS0030 active on `StoreOS.Domain`.

## P-119

### Reporting DbContext Seam
- Interface: `StoreOS.Application.Abstractions.IReportingDbContext` (`backend/src/StoreOS.Application/Abstractions/IReportingDbContext.cs`)
  ```csharp
  public interface IReportingDbContext
  {
      DbSet<TEntity> Set<TEntity>() where TEntity : class;
  }
  ```
- Implementation: `StoreOS.Infrastructure.Persistence.ReportingDbContext` (`backend/src/StoreOS.Infrastructure/Persistence/ReportingDbContext.cs`)
  ```csharp
  public class ReportingDbContext : DbContext, IReportingDbContext
  ```
  *Note: globally configures `QueryTrackingBehavior.NoTracking`, applies configurations from `StoreOsDbContext.Assembly`, and delegates to `QueryFilters.ApplyAll`.*
- Shared Query Filters: `StoreOS.Infrastructure.Persistence.QueryFilters` (`backend/src/StoreOS.Infrastructure/Persistence/QueryFilters.cs`)
  ```csharp
  public static class QueryFilters
  {
      public static void ApplyAll(ModelBuilder modelBuilder, Expression<Func<IAccessScope>> scopeAccessor);
      public static void ApplyAll(ModelBuilder modelBuilder, IAccessScope scope);
  }
  ```

### Query Filter Convention
Every later prompt that adds a new tenant-scoped or branch-scoped entity MUST add its HasQueryFilter(...) line HERE, in this single shared method, so both StoreOsDbContext and ReportingDbContext automatically stay consistent. Do not add query filters directly inside either DbContext's OnModelCreating from this point forward.
### DI Registration & Connection Strings (Program.cs)
- `builder.Services.AddDbContext<ReportingDbContext>` mapped to `ConnectionStrings:Reporting` with `RlsSessionInterceptor`.
- `builder.Services.AddScoped<IReportingDbContext, ReportingDbContext>()`.

### Transaction Behavior Resolution (P-109 / P-119)
- When `request is IReportingRequest`: resolves `ReportingDbContext`, opens `RepeatableRead` transaction, and issues `SET LOCAL transaction_read_only = on`.
- Otherwise: resolves `StoreOsDbContext` as before.

## P-201

### Tax Engine & Line Tax
- Struct: `StoreOS.Domain.Tax.LineTax` (`backend/src/StoreOS.Domain/Tax/LineTax.cs`)
  ```csharp
  public readonly record struct LineTax(decimal Taxable, decimal Cgst, decimal Sgst)
  {
      public decimal TotalGst => Cgst + Sgst;
      public static LineTax Zero => new(0m, 0m, 0m);
  }
  ```
- Engine: `StoreOS.Domain.Tax.TaxEngine` (`backend/src/StoreOS.Domain/Tax/TaxEngine.cs`)
  ```csharp
  public static class TaxEngine
  {
      public static LineTax StandardGst(decimal lineTotal, decimal gstRate);
  }
  ```

### GST Halves-Reconciliation Rule (Plan §7.1)
When splitting `totalGst` into CGST and SGST:
```csharp
var cgst = Money.Round2(totalGst / 2m);
var sgst = Money.Round2(totalGst - cgst);
```
`sgst` is always computed as `totalGst - cgst` (never `totalGst / 2m` independently). This guarantees `cgst + sgst == totalGst` exactly without odd-paise drift across the entire product.

## P-202

### Exceptions Location (P-113 revised build)
- Namespace: `StoreOS.Domain.Exceptions` (`backend/src/StoreOS.Domain/Exceptions/DomainException.cs`)
  ```csharp
  public class DomainException : Exception
  {
      public string Code { get; }
      public DomainException(string code, string message) : base(message) => Code = code;
  }

  public class DomainValidationException : DomainException
  {
      public DomainValidationException(string code, string message) : base(code, message) { }
  }
  ```

### Tax Engine: MarginSchemeGst
- Method: `TaxEngine.MarginSchemeGst` (`backend/src/StoreOS.Domain/Tax/TaxEngine.cs`)
  ```csharp
  public static LineTax MarginSchemeGst(decimal sell, decimal cost, decimal gstRate);
  ```
- Error Code: `margin_scheme_invalid_cost_basis`
  - Thrown when `cost <= 0` (strictly positive cost basis required).
  - Silent fallback to cost = 0 is prohibited under CGST Rule 32(5).
- Margin Rule:
  - If `margin <= 0` (`sell <= cost`), returns `LineTax.Zero`.
  - Taxable, CGST, and SGST halves calculated with `Money.Round2` (MidpointRounding.AwayFromZero) ensuring `cgst + sgst == totalGst`.

## P-203

### Discount Engine: ApplyProportionalDiscount (Plan §7.3)
- Class: `StoreOS.Domain.Tax.DiscountEngine` (`backend/src/StoreOS.Domain/Tax/DiscountEngine.cs`)
  ```csharp
  public static class DiscountEngine
  {
      /// <summary>
      /// Applies a proportional invoice-level discount to a line's tax components.
      /// subtotal represents the GROSS pre-discount invoice subtotal (sum of line totals),
      /// consistent with CheckoutHandler (P-510).
      /// </summary>
      public static LineTax ApplyProportionalDiscount(LineTax before, decimal subtotal, decimal discount);
  }
  ```
- Error Codes:
  - `discount_negative`: Thrown when `discount < 0`. Evaluated before the `subtotal == 0m` guard.
  - `discount_exceeds_subtotal`: Thrown when `discount > subtotal`.
- Invariants & Edge Cases:
  - `subtotal < 0` throws `ArgumentOutOfRangeException`.
  - `subtotal == 0m && discount == 0m` returns `before` unchanged (no-op, avoids divide-by-zero).
  - Proportional scaling factor `factor = (subtotal - discount) / subtotal` is applied to `Taxable`, `Cgst`, and `Sgst` using `Money.Round2`.

## P-204

### Finance Engine: CalculatePrincipal & CalculateEmi (Plan §7.4)
- Class: `StoreOS.Domain.Finance.FinanceEngine` (`backend/src/StoreOS.Domain/Finance/FinanceEngine.cs`)
  ```csharp
  public static class FinanceEngine
  {
      public static decimal CalculatePrincipal(decimal grandTotal, decimal downPayment);
      public static decimal CalculateEmi(decimal principal, decimal annualRatePercent, int months);
  }
  ```
- Invariant:
  - `DecimalPow` uses pure integer exponentiation via loop over `decimal`. `Math.Pow` is strictly banned (enforced via RS0030 diagnostic in `BannedSymbols.txt`).
- Error Codes:
  - `finance_down_payment_negative`: Thrown by `CalculatePrincipal` when `downPayment < 0`.
  - `finance_down_payment_exceeds_total`: Thrown by `CalculatePrincipal` when `downPayment > grandTotal`.
  - `finance_principal_not_positive`: Thrown by `CalculatePrincipal` when `principal <= 0`, and by `CalculateEmi` when `principal <= 0`.
  - `finance_invalid_tenure`: Thrown by `CalculateEmi` when `months <= 0`.
  - `finance_invalid_rate`: Thrown by `CalculateEmi` when `annualRatePercent < 0`.

## P-205

### Inventory Valuation (Plan / Requirements §11.5)
- Struct: `StoreOS.Domain.Inventory.StockValuationLine` (`backend/src/StoreOS.Domain/Inventory/StockValuationLine.cs`)
  ```csharp
  public readonly record struct StockValuationLine(string Category, int Qty, decimal PurchasePrice, decimal SellPrice);
  ```
- Struct: `StoreOS.Domain.Inventory.ValuationResult` (`backend/src/StoreOS.Domain/Inventory/ValuationResult.cs`)
  ```csharp
  public readonly record struct ValuationResult(decimal CostValuation, decimal RetailValuation, decimal PotentialGrossValue);
  ```
- Calculator: `StoreOS.Domain.Inventory.ValuationCalculator` (`backend/src/StoreOS.Domain/Inventory/ValuationCalculator.cs`)
  ```csharp
  public static class ValuationCalculator
  {
      public static ValuationResult Calculate(IEnumerable<StockValuationLine> lines);
      public static bool IsLowStock(int qty, int threshold);
      public static bool IsOutOfStock(int qty);
  }
  ```
- Invariants:
  - `"RepairService"` category string literal is excluded from stock valuation (services never count as stock, Invariant 3). Must match `products.category` CHECK constraint exactly (`'NewPhone','UsedPhone','Accessory','RepairService'`).
  - Low stock boundary: `qty <= threshold` (inclusive per Requirements §5.3).
  - Out of stock boundary: `qty <= 0` (inclusive).

## P-206

### Invoice Series & Formatting (Plan §8.5, DECISIONS D-07, DECISIONS O-01)
- Enum: `StoreOS.Domain.Sales.InvoiceSeriesFormat` (`backend/src/StoreOS.Domain/Sales/InvoiceSeriesFormat.cs`)
  ```csharp
  public enum InvoiceSeriesFormat
  {
      FyConsecutive,
      DailyTimestamp
  }
  ```
- Formatter: `StoreOS.Domain.Sales.InvoiceNumberFormatter` (`backend/src/StoreOS.Domain/Sales/InvoiceNumberFormatter.cs`)
  ```csharp
  public static class InvoiceNumberFormatter
  {
      public static string FormatFyConsecutive(string branchCode, string financialYearLabel, int sequence);
      public static string FormatEstimate(string branchCode, string financialYearLabel, int sequence);
      public static string FormatDailyTimestamp(string branchCode, DateTimeOffset businessDateTime, int dailySequence);
      public static string FinancialYearLabel(DateOnly businessDate);
  }
  ```
- Reference Character Count & Rule 46(b):
  - Reference format: `FormatFyConsecutive("B01", "26-27", 42)` -> `"B01/26-27/000042"`.
  - Confirmed exact length: 16 characters (`"B01"` [3] + `"/"` [1] + `"26-27"` [5] + `"/"` [1] + `"000042"` [6] = 16), strictly compliant with CGST Rule 46(b) max 16 chars.
  - Long branch codes resulting in formatted string length > 16 throw `DomainValidationException("invoice_number_exceeds_rule46b")`.
  - Estimates use format `$"{branchCode}/E/{financialYearLabel}/{sequence:D6}"` (e.g. `B01/E/26-27/000007`, 18 chars) and are exempt from the Rule 46(b) 16-character limit (DECISIONS D-07).
  - Daily timestamp format (23 chars) is an explicit legacy option carrying audit risk, exempt from in-code length enforcement (DECISIONS O-01).
- Financial Year Rollover:
  - Indian fiscal year runs April 1 through March 31.
  - Months >= 4 belong to fiscal start year; months < 4 belong to prior year.
  - Rollover occurs exactly on April 1 (e.g. 2027-04-01 -> "27-28", while 2027-02-10 -> "26-27").

## P-301

### Password Hashing (Argon2id)
- Interface: `StoreOS.Application.Identity.IPasswordHasher` (`backend/src/StoreOS.Application/Identity/IPasswordHasher.cs`)
  ```csharp
  namespace StoreOS.Application.Identity;

  public interface IPasswordHasher
  {
      string Hash(string plainTextPassword);
      bool Verify(string plainTextPassword, string storedHash);
  }
  ```
- Implementation: `StoreOS.Infrastructure.Identity.Argon2PasswordHasher` (`backend/src/StoreOS.Infrastructure/Identity/Argon2PasswordHasher.cs`)
  ```csharp
  namespace StoreOS.Infrastructure.Identity;

  public class Argon2PasswordHasher : IPasswordHasher
  {
      public string Hash(string plainTextPassword);
      public bool Verify(string plainTextPassword, string storedHash);
  }
  ```
- Stored-Hash String Format:
  - Format: `argon2id$<iterations>$<memoryKb>$<parallelism>$<base64salt>$<base64hash>`
  - Default Parameters:
    - `Iterations`: 3
    - `MemorySizeKb`: 65536 (64 MB)
    - `DegreeOfParallelism`: 4
    - `SaltSize`: 16 bytes (Base64-encoded)
    - `HashSize`: 32 bytes (Base64-encoded)
  - Verification: constant-time byte comparison via `CryptographicOperations.FixedTimeEquals`, returns `false` on any malformed/invalid input or format mismatch without throwing exceptions.
  - DI Registration: registered as singleton in `Program.cs`: `builder.Services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();`.

## P-302

### JWT Issuance Service & Bearer Authentication
- Contract: `StoreOS.Application.Abstractions.IJwtTokenService` (`backend/src/StoreOS.Application/Abstractions/IJwtTokenService.cs`)
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IJwtTokenService
  {
      (string token, DateTimeOffset expiresAt) IssueAccessToken(
          Guid userId, Guid tenantId, bool allBranches, IEnumerable<Guid> branchIds,
          IEnumerable<string> permissions, int tokenVersion);
  }
  ```
- Implementation: `StoreOS.Infrastructure.Identity.JwtTokenService` (`backend/src/StoreOS.Infrastructure/Identity/JwtTokenService.cs`)
  ```csharp
  namespace StoreOS.Infrastructure.Identity;

  public class JwtTokenService : IJwtTokenService
  {
      public JwtTokenService(IConfiguration config);
      public (string token, DateTimeOffset expiresAt) IssueAccessToken(
          Guid userId, Guid tenantId, bool allBranches, IEnumerable<Guid> branchIds,
          IEnumerable<string> permissions, int tokenVersion);
  }
  ```
- Configuration Keys (`appsettings.json`):
  - `Jwt:SigningKey`: `"dev-only-signing-key-change-in-production-min-32-bytes-long!!"` (dev placeholder; production deployment must source from cloud secret manager per Plan §14.4)
  - `Jwt:Issuer`: `"storeos-cloud"`
  - `Jwt:Audience`: `"storeos-cloud-clients"`
  - `Jwt:AccessTokenMinutes`: `15`
- Claims Match P-106 `HttpAccessScope` Exactly:
  - `sub`: user ID Guid string
  - `tenant_id`: tenant ID Guid string
  - `all_branches`: `"true"` or `"false"`
  - `branch_id`: branch ID Guid string (repeated claim when `all_branches` is false)
  - `perm`: permission string code (repeated claim)
  - `token_version`: integer token version string
- Confirmed Final Middleware Pipeline Order:
  1. `CorrelationIdMiddleware`
  2. `ExceptionHandlingMiddleware`
  3. `UseHttpsRedirection`
  4. `UseCors`
  5. `UseAuthentication` (with `JwtBearerDefaults.AuthenticationScheme` and `MapInboundClaims = false`)
  6. `UseAuthorization`
  7. `MapControllers` (MediatR behaviors handle `TransactionBehavior` and `IdempotencyBehavior` inside request scope per P-109, P-117)

## P-303

### Login Endpoint & Authentication Handler
- Command: `StoreOS.Application.Identity.Login.LoginCommand` (`backend/src/StoreOS.Application/Identity/Login/LoginCommand.cs`)
  ```csharp
  namespace StoreOS.Application.Identity.Login;

  public record LoginCommand(string Email, string Password, Guid TenantId)
      : IRequest<LoginResult>, IAnonymousTenantRequest;

  public record LoginResult(
      string AccessToken,
      DateTimeOffset ExpiresAt,
      Guid UserId,
      string DisplayName,
      Guid TenantId,
      bool AllBranches,
      IReadOnlyList<Guid> BranchIds,
      IReadOnlyList<string> Permissions);
  ```
- Validator: `StoreOS.Application.Identity.Login.LoginValidator` (`backend/src/StoreOS.Application/Identity/Login/LoginValidator.cs`)
- Handler: `StoreOS.Application.Identity.Login.LoginHandler` (`backend/src/StoreOS.Application/Identity/Login/LoginHandler.cs`)
  - Pre-auth tenant scoping: uses `IgnoreQueryFilters()` with `request.TenantId` since no `IAccessScope` exists yet.
  - Tenant-wide branch resolution: resolves `allBranches = totalAssignments > branchSpecificCount` by joining `user_role_assignments` with `branches` (handling the null branch_id sentinel pattern).
- Controller: `StoreOS.Api.Controllers.AuthController` (`backend/src/StoreOS.Api/Controllers/AuthController.cs`)
  - Route: `POST /api/auth/login` (`[AllowAnonymous]`)
  - Fallback Tenant Resolution: if `command.TenantId == Guid.Empty`, resolves via `ITenantResolver.ResolveFromHostAsync(Request.Host.Value)` or returns 422 `tenant_not_found`.
  - Response 200 OK JSON:
    ```json
    {
      "accessToken": "ey...",
      "expiresAt": "2026-10-05T...",
      "userId": "uuid",
      "displayName": "string",
      "tenantId": "uuid",
      "allBranches": true,
      "branchIds": ["uuid"],
      "permissions": ["pos:checkout"]
    }
    ```
- Security & Anti-Enumeration Rules:
  - All credential failure conditions (wrong password, unknown email, inactive user) return identical generic error: 401 Unauthorized with code `"invalid_credentials"` and message `"Invalid email or password."`.
  - Inactive tenant returns 403 Forbidden with code `"tenant_inactive"`.
  - Error status mappings registered in `ErrorCatalog.cs`.

## P-304

### Refresh Token Issuance & Cookie Handling on Login
- Token Formatting & Hashing: `StoreOS.Application.Identity.RefreshTokenFormat` (`backend/src/StoreOS.Application/Identity/RefreshTokenFormat.cs`)
  ```csharp
  namespace StoreOS.Application.Identity;

  public static class RefreshTokenFormat
  {
      public static string Compose(Guid tenantId, byte[] secret);
      public static bool TryParseTenantId(string? raw, out Guid tenantId);
      public static string Hash(string raw);
  }
  ```
- Contract: `StoreOS.Application.Abstractions.IRefreshTokenService` (`backend/src/StoreOS.Application/Abstractions/IRefreshTokenService.cs`)
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IRefreshTokenService
  {
      Task<(string rawToken, RefreshToken record)> IssueAsync(
          Guid tenantId, Guid userId, Guid familyId, CancellationToken ct);
  }
  ```
- Implementation: `StoreOS.Infrastructure.Identity.RefreshTokenService` (`backend/src/StoreOS.Infrastructure/Identity/RefreshTokenService.cs`)
  ```csharp
  namespace StoreOS.Infrastructure.Identity;

  public class RefreshTokenService : IRefreshTokenService
  {
      public RefreshTokenService(StoreOsDbContext db);
      public Task<(string rawToken, RefreshToken record)> IssueAsync(
          Guid tenantId, Guid userId, Guid familyId, CancellationToken ct);
  }
  ```
- Internal Login Result Extension: `StoreOS.Application.Identity.Login.LoginResult`
  ```csharp
  public record LoginResult(
      string AccessToken,
      DateTimeOffset ExpiresAt,
      Guid UserId,
      string DisplayName,
      Guid TenantId,
      bool AllBranches,
      IReadOnlyList<Guid> BranchIds,
      IReadOnlyList<string> Permissions,
      string RawRefreshToken);
  ```
- Refresh Token Cookie Configuration (`AuthController.Login`):
  - Cookie Name: `storeos_refresh_token`
  - Path: `/api/auth`
  - HttpOnly: `true`
  - Secure: `true` (HTTPS only)
  - SameSite: `SameSiteMode.Strict`
  - Lifetime: 30 days (`DateTimeOffset.UtcNow.AddDays(30)`)
- Security & Anti-Leak Invariants:
  - Raw token format: `"{tenantId:N}.{base64url(32 random bytes)}"`.
  - Database stores ONLY SHA-256 hex hash (`RefreshTokenFormat.Hash(rawToken)`), never raw token.
  - `RawRefreshToken` is transmitted exclusively through the `HttpOnly` cookie and is NEVER included in response JSON, logs, or audit records.

## P-305

### Refresh Endpoint & Token Family Theft/Replay Detection
- Command: `StoreOS.Application.Identity.Refresh.RefreshCommand` (`backend/src/StoreOS.Application/Identity/Refresh/RefreshCommand.cs`)
  ```csharp
  namespace StoreOS.Application.Identity.Refresh;

  public record RefreshCommand(string RawRefreshToken) : IRequest<RefreshResult>, IAnonymousTenantRequest
  {
      public Guid TenantId => RefreshTokenFormat.TryParseTenantId(RawRefreshToken, out var t) ? t : Guid.Empty;
  }
  ```
- Result: `StoreOS.Application.Identity.Refresh.RefreshResult` (`backend/src/StoreOS.Application/Identity/Refresh/RefreshCommand.cs`)
  ```csharp
  public record RefreshResult(
      string AccessToken,
      DateTimeOffset ExpiresAt,
      string NewRawRefreshToken,
      Guid UserId,
      string DisplayName,
      Guid TenantId,
      bool AllBranches,
      IReadOnlyList<Guid> BranchIds,
      IReadOnlyList<string> Permissions,
      bool ReuseDetected = false);
  ```
- Handler: `StoreOS.Application.Identity.Refresh.RefreshHandler` (`backend/src/StoreOS.Application/Identity/Refresh/RefreshHandler.cs`)
- Controller Route: `POST /api/auth/refresh` (`[AllowAnonymous]`) in `StoreOS.Api.Controllers.AuthController`
  - Input: Cookie `storeos_refresh_token`
  - Output on Success: 200 OK with refreshed `accessToken`, `expiresAt`, `userId`, `displayName`, `tenantId`, `allBranches`, `branchIds`, `permissions`, and updated `storeos_refresh_token` cookie.
- Replay / Theft Detection Protocol (Plan §8.3):
  - When a presented token has `IsRevoked == true` (it was previously rotated into a successor token):
    1. Replay is detected: all tokens sharing the same `FamilyId` are immediately marked `IsRevoked = true`.
    2. The user's `TokenVersion` is incremented (`affectedUser.BumpTokenVersion()`), invalidating all active JWTs upon stale check.
    3. Changes are committed to the DB via `await _db.SaveChangesAsync(ct);`.
    4. `RefreshResult` is returned with `ReuseDetected = true`.
    5. Controller deletes `storeos_refresh_token` cookie and returns 401 Unauthorized with code `"refresh_token_reuse_detected"`.
  - Token rotation maintains the SAME `FamilyId` across valid rotations, linking `replaced_by_token_id`.
  - Expired tokens throw `DomainException("refresh_token_expired")` (mapped to 401).
  - Invalid tokens or tokens for wrong tenant throw `DomainException("invalid_refresh_token")` (mapped to 401).

## P-306

### Logout Endpoint & Current Identity ("Me") Endpoint
- Command: `StoreOS.Application.Identity.Logout.LogoutCommand` (`backend/src/StoreOS.Application/Identity/Logout/LogoutCommand.cs`)
  ```csharp
  namespace StoreOS.Application.Identity.Logout;

  public record LogoutCommand(string RawRefreshToken) : IRequest, IAnonymousTenantRequest
  {
      public Guid TenantId => RefreshTokenFormat.TryParseTenantId(RawRefreshToken, out var t) ? t : Guid.Empty;
  }
  ```
- Handler: `StoreOS.Application.Identity.Logout.LogoutHandler` (`backend/src/StoreOS.Application/Identity/Logout/LogoutHandler.cs`)
  - Revokes only the presented refresh token by hash (`token?.Revoke()`), preserving the rest of the family.
  - Idempotent: non-existent or already revoked tokens still succeed without error.
- Controller Routes (`StoreOS.Api.Controllers.AuthController`):
  - `POST /api/auth/logout`:
    - Requires authentication by default (inherits FallbackPolicy).
    - Checks `RefreshTokenFormat.TryParseTenantId(rawToken, out _)` before sending `LogoutCommand` to ensure safe anonymous/fallback handling.
    - Deletes `storeos_refresh_token` cookie (path `/api/auth`).
    - Returns 204 NoContent.
  - `GET /api/auth/me`:
    - Requires authentication by default (inherits FallbackPolicy).
    - Response 200 OK JSON:
      ```json
      {
        "userId": "uuid",
        "tenantId": "uuid",
        "allBranches": true,
        "branchIds": ["uuid"],
        "permissions": ["string"],
        "tokenVersion": 1
      }
      ```
- Global Authorization Baseline (Fallback Policy):
  - Configured in `Program.cs`:
    ```csharp
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
    });
    ```
  - By default, ALL controller endpoints require an authenticated user unless explicitly annotated with `[AllowAnonymous]`.
- TransactionBehavior Tenant Switch Fix:
  - `TransactionBehavior` checks `if (!_scope.IsAuthenticated)` before calling `_scope.UseAnonymousTenant(anon.TenantId)`, allowing authenticated commands implementing `IAnonymousTenantRequest` (such as `LogoutCommand`) to execute safely without throwing `InvalidOperationException`.

## P-307

### Token Version Revocation Check (30s Cached Stale-JWT Invalidation)
- Pipeline Behavior: `StoreOS.Application.Behaviors.TokenVersionBehavior<TRequest, TResponse>` (`backend/src/StoreOS.Application/Behaviors/TokenVersionBehavior.cs`)
  ```csharp
  namespace StoreOS.Application.Behaviors;

  public class TokenVersionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
      where TRequest : IRequest<TResponse>
  {
      public TokenVersionBehavior(IAccessScope scope, IAppDbContext db, IMemoryCache cache);
      public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
  }
  ```
- Cache Configuration & TTL:
  - Caching Provider: `IMemoryCache` (registered via `builder.Services.AddMemoryCache();` in `Program.cs`).
  - Cache Key Format: `"token_version:{userId}"`
  - Cache TTL: 30 seconds (`CacheTtl = TimeSpan.FromSeconds(30)`).
  - Tradeoff Guarantee (Plan §8.3): Bumping `users.token_version` in the database immediately invalidates sessions upon cache expiration (up to 30-second window). High-impact critical operations utilize live database checks (P-308).
- Pipeline Execution Position in `Program.cs`:
  1. `AuthorizationBehavior<,>`
  2. `ValidationBehavior<,>`
  3. `TransactionBehavior<,>` (opens transaction and establishes RLS `app.tenant_id`)
  4. `TokenVersionBehavior<,>` (runs inside transaction scope to query user under tenant RLS)
  5. `IdempotencyBehavior<,>`
- Architectural Note & Documented Gap:
  - This check runs strictly within the MediatR request pipeline.
  - Plain controller actions that read `IAccessScope` directly without dispatching a MediatR command or query (e.g. `GET /api/auth/me`) bypass this staleness check. All application domain logic and business operations must be routed via MediatR to ensure uniform revocation enforcement.

## P-308

### High-Impact Operation Live-DB Permission Re-Check Behavior
- Marker Interface: `StoreOS.Application.Abstractions.IHighImpactOperation` (`backend/src/StoreOS.Application/Abstractions/IHighImpactOperation.cs`)
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IHighImpactOperation
  {
      string RequiredPermission { get; }
  }
  ```
- Pipeline Behavior: `StoreOS.Application.Behaviors.LiveDbPermissionBehavior<TRequest, TResponse>` (`backend/src/StoreOS.Application/Behaviors/LiveDbPermissionBehavior.cs`)
  ```csharp
  namespace StoreOS.Application.Behaviors;

  public class LiveDbPermissionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
      where TRequest : IRequest<TResponse>
  {
      public LiveDbPermissionBehavior(IAccessScope scope, IAppDbContext db);
      public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
  }
  ```
- Full MediatR Behavior Pipeline Registration Order (`Program.cs`):
  1. `AuthorizationBehavior<,>` (claims-based check on JWT, no DB)
  2. `ValidationBehavior<,>` (field validation, 400 Bad Request, no DB)
  3. `TransactionBehavior<,>` (opens transaction and establishes RLS `app.tenant_id`)
  4. `LiveDbPermissionBehavior<,>` (zero-cache live DB permission & user activity re-check under RLS transaction)
  5. `TokenVersionBehavior<,>` (30s cached token version revocation check under RLS transaction)
  6. `IdempotencyBehavior<,>` (idempotency key verification inside transaction)
  7. Request Handler
- Critical Design Rules:
  - **At Most One Rule**: A request command must implement at most ONE of `IRequiresPermission` or `IHighImpactOperation`, never both.
  - **Zero-Cache Guarantee**: `LiveDbPermissionBehavior` intentionally bypasses all caching layers (`IMemoryCache`, distributed caches, and JWT claim cache), querying `user_role_assignments`, `role_permissions`, and `users.is_active` fresh from the database on every single invocation.
  - **Target High-Impact Commands**: The following sensitive domain operations MUST implement `IHighImpactOperation`:
    - `sales:void` (`VoidSaleCommand`, P-524)
    - `sales:refund` (`RefundSaleCommand`, P-525)
    - `inventory:adjustments` (`InventoryAdjustmentCommand`, P-413)
    - `settings:manage` (Settings management commands, P-1120)
    - `branches:manage` (Branch management commands, P-310)

## P-309

### X-Branch-Id Middleware & Branch Access Enforcement
- Interface Contract: `StoreOS.Application.Abstractions.IBranchContext` (`backend/src/StoreOS.Application/Abstractions/IBranchContext.cs`)
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IBranchContext
  {
      Guid? CurrentBranchId { get; }
      bool HasBranchContext { get; }
  }
  ```
- Scoped Implementation: `StoreOS.Infrastructure.Security.MutableBranchContext` (`backend/src/StoreOS.Infrastructure/Security/MutableBranchContext.cs`)
  ```csharp
  namespace StoreOS.Infrastructure.Security;

  public class MutableBranchContext : IBranchContext
  {
      public Guid? CurrentBranchId { get; set; }
      public bool HasBranchContext => CurrentBranchId.HasValue;
  }
  ```
- Middleware: `StoreOS.Api.Middleware.BranchContextMiddleware` (`backend/src/StoreOS.Api/Middleware/BranchContextMiddleware.cs`)
  - Header Constant: `public const string HeaderName = "X-Branch-Id";`
  - HTTP Pipeline Order:
    1. `CorrelationIdMiddleware`
    2. `ExceptionHandlingMiddleware`
    3. `UseHttpsRedirection`
    4. `UseCors`
    5. `UseAuthentication`
    6. `UseAuthorization`
    7. `BranchContextMiddleware` (runs after auth/scope resolution and before `MapControllers`)
    8. `MapControllers`
  - Enforcement Rules:
    - Bypasses non-authenticated requests and `/api/auth/*` authentication endpoints (login, refresh, logout).
    - Reads `X-Branch-Id` header if present.
    - If header is absent: falls back to user's default branch via `user_branches.is_default` inside a read transaction.
    - If branch is null AND request is a mutation (`POST`, `PUT`, `PATCH`, `DELETE`): throws `DomainException("branch_context_required")` (400 Bad Request). Non-mutation reads (`GET`, `HEAD`) allow `null` branch context.
    - If branch is non-null: validates `IAccessScope.HasBranchAccess(branchId.Value)` and throws `DomainException("forbidden")` (403 Forbidden) if unauthorized.
- Conceptual Invariant & Separation of Concerns (Plan §9.3):
  - `IBranchContext.CurrentBranchId`: The **single, explicit branch** that this specific request/mutation is operating against. All new records stamped with `branch_id` use this value. There is no branchless write.
  - `IAccessScope.BranchIds` / `AllBranches`: The user's **full accessible set of branches** across the tenant, used exclusively for session-level PostgreSQL RLS policies (`app.branch_ids`, `app.all_branches`).

## P-310

### Branches CRUD Endpoints & Vertical Slice Architecture
- Data Transfer Object: `StoreOS.Application.Tenancy.Branches.BranchDto` (`backend/src/StoreOS.Application/Tenancy/Branches/BranchDto.cs`)
  ```csharp
  namespace StoreOS.Application.Tenancy.Branches;

  public record BranchDto(
      Guid Id,
      string Code,
      string Name,
      string? Address,
      string? Phone,
      string? Email,
      string? Gstin,
      bool IsActive);
  ```
- Vertical Slices (`StoreOS.Application/Tenancy/Branches/`):
  - `CreateBranch/`:
    - Command: `CreateBranchCommand(string Code, string Name, string? Address, string? Phone, string? Email, string? Gstin)` (`IRequest<Guid>`, `IHighImpactOperation`, `RequiredPermission => "branches:manage"`)
    - Validator: `CreateBranchValidator` (Code regex `^[A-Z1-9][A-Z0-9]{0,2}$` max 3 chars Rule 46(b) budget, Name required, optional Email)
    - Handler: `CreateBranchHandler` (checks code uniqueness, creates entity, updates profile, saves)
  - `GetBranches/`:
    - Query: `GetBranchesQuery()` (`IRequest<IReadOnlyList<BranchDto>>`, `IReadOnlyRequest`)
    - Handler: `GetBranchesHandler` (filters by tenant and `_scope.BranchIds` when `!_scope.AllBranches`)
  - `GetBranchById/`:
    - Query: `GetBranchByIdQuery(Guid Id)` (`IRequest<BranchDto>`, `IReadOnlyRequest`)
    - Handler: `GetBranchByIdHandler` (validates `_scope.HasBranchAccess`, returns `BranchDto` or throws `branch_not_found`)
  - `UpdateBranch/`:
    - Command: `UpdateBranchCommand(Guid Id, string Name, string? Address, string? Phone, string? Email, string? Gstin, bool IsActive)` (`IRequest`, `IHighImpactOperation`, `RequiredPermission => "branches:manage"`)
    - Validator: `UpdateBranchValidator`
    - Handler: `UpdateBranchHandler` (updates profile, toggles `Activate`/`Deactivate` based on `IsActive`)
- API Controller: `StoreOS.Api.Controllers.BranchesController` (`backend/src/StoreOS.Api/Controllers/BranchesController.cs`)
  - Routes:
    - `GET /api/branches` -> List (returns `IReadOnlyList<BranchDto>`)
    - `POST /api/branches` -> Create (returns 201 `CreatedAtAction` with `new { id }`)
    - `GET /api/branches/{id:guid}` -> GetById (returns `BranchDto` or 404)
    - `PATCH /api/branches/{id:guid}` -> Update with `UpdateBranchBody` (returns 204 `NoContent`)
- Error Catalog Status Mapping:
  - `branch_code_taken` -> 409 Conflict (`ErrorCatalog.cs`)
