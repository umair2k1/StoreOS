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
