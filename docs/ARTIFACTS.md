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

## P-311

### Users/Roles/Branch-Assignment Administration
- Roles Slices (`StoreOS.Application/Identity/Roles/`):
  - `RoleDto`: `record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions)`
  - `CreateRole/`:
    - Command: `CreateRoleCommand(string Name, IReadOnlyList<string> PermissionCodes)` (`IRequest<Guid>`, `IHighImpactOperation`, `RequiredPermission => "users:manage"`)
    - Validator: `CreateRoleValidator` (Name required max 100, PermissionCodes not null)
    - Handler: `CreateRoleHandler` (validates all permission codes via `IPermissionChecker.IsValidPermissionCode`, throws `DomainValidationException("invalid_permission_code")` on invalid codes, creates role & distinct `RolePermission` rows)
  - `GetRoles/`:
    - Query: `GetRolesQuery()` (`IRequest<IReadOnlyList<RoleDto>>`, `IRequiresPermission`, `RequiredPermission => "users:manage"`, `IReadOnlyRequest`)
    - Handler: `GetRolesHandler` (lists tenant roles with permissions mapped to `RoleDto`)
- Users Slices (`StoreOS.Application/Identity/Users/`):
  - `UserDto`: `record UserDto(Guid Id, string Email, string DisplayName, bool IsActive, int TokenVersion, IReadOnlyList<UserRoleAssignmentDto> Roles, IReadOnlyList<UserBranchAssignmentDto> Branches)`
  - `CreateUser/`:
    - Command: `CreateUserCommand(string Email, string DisplayName, string Password, Guid? InitialRoleId = null, Guid? InitialBranchId = null)` (`IRequest<Guid>`, `IHighImpactOperation`, `RequiredPermission => "users:manage"`)
    - Validator: `CreateUserValidator` (Email valid, DisplayName max 100, Password min 8)
    - Handler: `CreateUserHandler` (validates email uniqueness, hashes password with `IPasswordHasher`, creates user, binds initial role & branch)
  - `GetUsers/`:
    - Query: `GetUsersQuery()` (`IRequest<IReadOnlyList<UserDto>>`, `IRequiresPermission`, `RequiredPermission => "users:manage"`, `IReadOnlyRequest`)
    - Handler: `GetUsersHandler` (lists tenant users with assigned roles & branches, uses left join to safely project `BranchId`)
  - `AssignRole/`:
    - Command: `AssignRoleCommand(Guid UserId, Guid RoleId, Guid? BranchId = null)` (`IRequest`, `IHighImpactOperation`, `RequiredPermission => "users:manage"`)
    - Validator: `AssignRoleValidator` (UserId, RoleId required)
    - Handler: `AssignRoleHandler` (validates user, role, and branch within tenant, assigns role)
  - `AssignBranch/`:
    - Command: `AssignBranchCommand(Guid UserId, Guid BranchId, bool IsDefault = false)` (`IRequest`, `IHighImpactOperation`, `RequiredPermission => "users:manage"`)
    - Validator: `AssignBranchValidator` (UserId, BranchId required)
    - Handler: `AssignBranchHandler` (validates user and branch within tenant, manages `is_default` flag, inserts or updates `UserBranch`)
  - `DeactivateUser/`:
    - Command: `DeactivateUserCommand(Guid UserId)` (`IRequest`, `IHighImpactOperation`, `RequiredPermission => "users:manage"`)
    - Validator: `DeactivateUserValidator` (UserId required)
    - Handler: `DeactivateUserHandler` (calls `user.Deactivate()`, bumping `TokenVersion` and invalidating sessions)
- Controllers (`StoreOS.Api.Controllers/`):
  - `RolesController`:
    - `GET /api/roles` -> List
    - `POST /api/roles` -> Create (201 Created)
  - `UsersController`:
    - `GET /api/users` -> List
    - `POST /api/users` -> Create (201 Created)
    - `POST /api/users/{id}/roles` -> AssignRole (204 NoContent)
    - `POST /api/users/{id}/branches` -> AssignBranch (204 NoContent)
    - `POST /api/users/{id}/deactivate` -> Deactivate (204 NoContent)
- Error Catalog Status Mapping:
  - `invalid_permission_code` -> 400 Bad Request (`ErrorCatalog.cs`)
- Abstraction Contract Extension:
  - `IAppDbContext.ExecuteRawSqlAsync(string sql, object[] parameters, CancellationToken ct = default)` implemented in `StoreOsDbContext` for raw SQL commands.

## P-312

### Rate Limiting Policies & Rejection Handling
- Rate Limiting Middleware (`Program.cs`):
  - Service Registration: `builder.Services.AddRateLimiter(...)`
  - Pipeline Position:
    1. `CorrelationIdMiddleware`
    2. `ExceptionHandlingMiddleware`
    3. `app.UseRateLimiter();` (enforces rate limits prior to authentication & handler execution)
    4. `UseHttpsRedirection`
    5. `UseCors`
    6. `UseAuthentication`
    7. `UseAuthorization`
    8. `BranchContextMiddleware`
    9. `MapControllers`
- Rate Limiting Policies:
  1. Named Policy `"login"`:
     - Target: `AuthController.Login` via `[EnableRateLimiting("login")]`
     - Partition Key: `context.Connection.RemoteIpAddress?.ToString() ?? "unknown"` (IP-based)
     - Algorithm: Fixed Window
     - Limit: 5 requests per 1-minute window (`PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0`)
  2. Global Limiter (`options.GlobalLimiter`):
     - Target: All incoming HTTP endpoints
     - Partition Key: `context.User?.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous"` (User ID when authenticated, IP fallback when anonymous)
     - Algorithm: Fixed Window
     - Limit: 200 requests per 1-minute window (`PermitLimit = 200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0`)
- Rejection Response Shape (`options.OnRejected`):
  - Status Code: 429 Too Many Requests (`StatusCodes.Status429TooManyRequests`)
  - Content-Type: `application/problem+json`
  - JSON Payload:
    ```json
    {
      "status": 429,
      "title": "Too many requests. Please slow down.",
      "code": "rate_limited"
    }
    ```

## P-313

### User Password Management & Admin Password Reset
- Vertical Slices (`StoreOS.Application/Identity/Password/`):
  - `ChangePassword/`:
    - Command: `StoreOS.Application.Identity.Password.ChangePassword.ChangePasswordCommand(string CurrentPassword, string NewPassword)` (`IRequest`)
    - Validator: `ChangePasswordValidator` (CurrentPassword not empty, NewPassword min 10 chars, at least one letter and digit, must differ from current)
    - Handler: `ChangePasswordHandler` (verifies current password via `IPasswordHasher`, updates hash, bumps `TokenVersion`, evicts `IMemoryCache` entry, revokes all user refresh tokens, writes audit `user.password.changed`)
  - `AdminResetPassword/`:
    - Command: `StoreOS.Application.Identity.Password.AdminResetPassword.AdminResetPasswordCommand(Guid UserId, string NewTemporaryPassword)` (`IRequest`, `IHighImpactOperation`, `RequiredPermission => "users:manage"`)
    - Validator: `AdminResetPasswordValidator` (UserId not empty, NewTemporaryPassword min 10 chars, letter and digit)
    - Handler: `AdminResetPasswordHandler` (validates Owner hierarchy: Owner can only be reset by another tenant-wide Owner; updates hash, bumps `TokenVersion`, evicts `IMemoryCache`, revokes target user's refresh tokens, writes audit `user.password.reset`)
- API Controller Routes:
  - `POST /api/auth/change-password` (`AuthController`):
    - `[Authorize]`, `[EnableRateLimiting("login")]`
    - Clears `storeos_refresh_token` cookie and returns 204 `NoContent`.
  - `POST /api/users/{id:guid}/reset-password` (`UsersController`):
    - Requires `users:manage` permission via `LiveDbPermissionBehavior`.
    - Returns 204 `NoContent`.
- Audit Action Names:
  - `"user.password.changed"` (for user self-change; `changes` JSON never contains password material)
  - `"user.password.reset"` (for admin reset; `changes` JSON never contains password material)
- Error Catalog Status Mapping:
  - `current_password_incorrect` -> 422 Unprocessable Entity (`ErrorCatalog.cs`)
- Security & Session Invalidation Invariant:
  - Every password change or reset immediately bumps `users.token_version`, evicts the in-memory cache key (`token_version:{userId}`), and revokes all active refresh tokens in the database, ensuring all existing sessions are invalidated across devices.

## P-401

### Catalog & Product Entity
- Domain Entity (`StoreOS.Domain.Catalog.Product`):
  ```csharp
  public class Product
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public string Name { get; private set; }
      public string Category { get; private set; }
      public decimal SellPrice { get; private set; }
      public decimal PurchasePrice { get; private set; }
      public decimal GstRate { get; private set; }
      public bool IsTaxable { get; private set; }
      public bool IsMarginScheme { get; private set; }
      public string? HsnCode { get; private set; }
      public string? Description { get; private set; }
      public int? EstimatedDurationMinutes { get; private set; }
      public int? WarrantyDays { get; private set; }
      public bool IsActive { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }

      public static Product Create(Guid tenantId, string name, string category, decimal sellPrice, decimal purchasePrice, decimal gstRate, string? hsnCode);
      public void SetMarginScheme(bool enabled);
      public void UpdatePricing(decimal sellPrice, decimal purchasePrice, decimal gstRate);
      public void UpdateDescriptionAndServiceFields(string? description, int? estimatedDurationMinutes, int? warrantyDays);
      public void Rename(string name);
      public void Deactivate();
      public void Activate();
  }
  ```
- Categories (`StoreOS.Domain.Catalog.ProductCategory`):
  ```csharp
  public static class ProductCategory
  {
      public const string NewPhone = "NewPhone";
      public const string UsedPhone = "UsedPhone";
      public const string Accessory = "Accessory";
      public const string RepairService = "RepairService";
      public static readonly IReadOnlyList<string> All = new[] { NewPhone, UsedPhone, Accessory, RepairService };
      public static bool IsValid(string category);
      public static bool IsPhysical(string category);
  }
  ```
- Database Schema & Invariants:
  - Table: `public.products` (columns: `id` uuid PK, `tenant_id` uuid NOT NULL, `name` text NOT NULL, `category` text NOT NULL, `sell_price` numeric(18,2) NOT NULL, `purchase_price` numeric(18,2) NOT NULL DEFAULT 0, `gst_rate` numeric(5,2) NOT NULL DEFAULT 18, `is_taxable` bool NOT NULL DEFAULT true, `is_margin_scheme` bool NOT NULL DEFAULT false, `hsn_code` text, `description` text, `estimated_duration_minutes` int, `warranty_days` int, `is_active` bool NOT NULL DEFAULT true, `created_at` timestamptz NOT NULL DEFAULT now()).
  - Indexes: `ix_products_tenant_cat` on `(tenant_id, category)`, `ix_products_tenant_name` on `(tenant_id, name)`.
  - Constraints: `ck_products_category` CHECK (`category IN ('NewPhone','UsedPhone','Accessory','RepairService')`).
  - RLS: Enabled & Forced with `tenant_isolation_policy` (`tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid`).
  - Inventory Invariant 3 Rule: `RepairService` products intentionally do NOT get `branch_inventory` rows (enforced from P-403 onward).
- Migration:
  - `20261005180716_009_Products` applied via `dotnet ef migrations add 009_Products --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`.
## P-402

### Reusable Pagination & Products Catalog CRUD
- Reusable Paged Result (`StoreOS.Application.Abstractions`):
  ```csharp
  public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
  ```
- Product DTO (`StoreOS.Application.Catalog.Products`):
  ```csharp
  public record ProductDto(
      Guid Id,
      string Name,
      string Category,
      decimal SellPrice,
      decimal PurchasePrice,
      decimal GstRate,
      bool IsTaxable,
      bool IsMarginScheme,
      string? HsnCode,
      string? Description,
      int? EstimatedDurationMinutes,
      int? WarrantyDays,
      bool IsActive);
  ```
- Vertical Slices (`StoreOS.Application.Catalog.Products`):
  - `CreateProduct`:
    - Command: `CreateProductCommand(string Name, string Category, decimal SellPrice, decimal PurchasePrice, decimal GstRate, string? HsnCode, string? Description = null, int? EstimatedDurationMinutes = null, int? WarrantyDays = null, bool IsMarginScheme = false) : IRequest<Guid>, IRequiresPermission` (`inventory:manage`)
    - Validator: `CreateProductValidator` (validates Name not empty, Category valid via `ProductCategory.IsValid`, non-negative prices and GST)
    - Handler: `CreateProductHandler`
  - `UpdateProduct`:
    - Command: `UpdateProductCommand(Guid Id, string? Name = null, decimal? SellPrice = null, decimal? PurchasePrice = null, decimal? GstRate = null, bool? IsMarginScheme = null, string? Description = null, int? EstimatedDurationMinutes = null, int? WarrantyDays = null, bool? IsActive = null) : IRequest, IRequiresPermission` (`inventory:manage`)
    - Validator: `UpdateProductValidator` (validates Id not empty, non-empty Name if provided, non-negative prices and GST)
    - Handler: `UpdateProductHandler`
  - `GetProducts`:
    - Query: `GetProductsQuery(string? Category = null, bool? IsActive = null, bool? IsMarginScheme = null, string? Search = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<ProductDto>>, IReadOnlyRequest` (no special permission required; POS-browsable)
    - Handler: `GetProductsHandler`
  - `GetProductById`:
    - Query: `GetProductByIdQuery(Guid Id) : IRequest<ProductDto>, IReadOnlyRequest`
    - Handler: `GetProductByIdHandler`
- API Controller (`StoreOS.Api.Controllers.ProductsController`):
  - `GET /api/products` -> `List([FromQuery] GetProductsQuery query)`
  - `GET /api/products/{id:guid}` -> `GetById(Guid id)`
  - `POST /api/products` -> `Create([FromBody] CreateProductCommand command)` (returns 201 CreatedAtAction)
  - `PATCH /api/products/{id:guid}` -> `Update(Guid id, [FromBody] UpdateProductBody body)` (returns 204 NoContent)

## P-403

### Branch Inventory Entity & Archetype B Policy
- Domain Entity (`StoreOS.Domain.Inventory.BranchInventory`):
  ```csharp
  public class BranchInventory
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid BranchId { get; private set; }
      public Guid ProductId { get; private set; }
      public int StockQty { get; private set; }
      public decimal CostPrice { get; private set; }
      public decimal? SellPriceOverride { get; private set; }
      public int LowStockThreshold { get; private set; }

      public static BranchInventory Create(Guid tenantId, Guid branchId, Guid productId);
      public void IncreaseStock(int qty, decimal? newCostPrice = null);
      public void DecreaseStock(int qty);
      public void SetSellPriceOverride(decimal? price);
      public void SetLowStockThreshold(int threshold);
      public decimal ResolveSellPrice(decimal productSellPrice);
  }
  ```
- Concurrency & Stock Mutation Invariant:
  - `IncreaseStock`/`DecreaseStock` entity methods are for in-memory operations and invariant protection (`StockQty >= 0`).
  - Production stock mutations MUST go through `IInventoryStockService` (introduced in P-406) via atomic conditional SQL (`WHERE stock_qty >= @qty`) to prevent concurrency races.
- RepairService Invariant 3:
  - `RepairService` products intentionally NEVER get `branch_inventory` rows. Enforced across catalog/inventory flows and asserted by P-905 nightly reconciliation.
- Database Schema & Archetype B RLS:
  - Table: `public.branch_inventory`
    - Columns: `id` uuid PK, `tenant_id` uuid NOT NULL, `branch_id` uuid NOT NULL REFERENCES branches(id), `product_id` uuid NOT NULL REFERENCES products(id), `stock_qty` int NOT NULL DEFAULT 0, `cost_price` numeric(18,2) NOT NULL DEFAULT 0, `sell_price_override` numeric(18,2) NULL, `low_stock_threshold` int NOT NULL DEFAULT 3.
    - Constraints: `ck_branch_inventory_stock_qty` CHECK (`stock_qty >= 0`), `ix_branch_inventory_tenant_branch_product` UNIQUE (`tenant_id`, `branch_id`, `product_id`).
    - RLS Policy (`tenant_branch_isolation_policy`):
      ```sql
      ALTER TABLE public.branch_inventory ENABLE ROW LEVEL SECURITY;
      ALTER TABLE public.branch_inventory FORCE ROW LEVEL SECURITY;
      CREATE POLICY tenant_branch_isolation_policy ON public.branch_inventory USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
        AND (
          COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
          OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
        )
      );
      ```
- Migration:
  - `20261005181632_010_BranchInventory` applied via `dotnet ef migrations add 010_BranchInventory --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`.
## P-404

### Inventory Reporting Queries & Valuation
- Query Slices (`StoreOS.Application.Inventory.Queries`):
  - `GetInventory`:
    - Query: `GetInventoryQuery(string? Category = null, string? StockStatus = null, bool? IsMarginScheme = null, string? Search = null, Guid[]? BranchIds = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<InventoryLineDto>>, IReportingRequest`
    - Handler: `GetInventoryHandler` (injects `IReportingDbContext`; translates `StockStatus` filter into SQL predicate before pagination; maps `EffectiveSellPrice` via `SellPriceOverride ?? ProductSellPrice`)
  - `GetLowStock`:
    - Query: `GetLowStockQuery(Guid[]? BranchIds = null) : IRequest<IReadOnlyList<InventoryLineDto>>, IReportingRequest`
    - Handler: `GetLowStockHandler` (injects `IReportingDbContext`; returns low/out-of-stock items `stock_qty <= low_stock_threshold` across accessible branches; defensively filters out `RepairService`)
  - `GetValuation`:
    - Query: `GetValuationQuery(Guid[]? BranchIds = null) : IRequest<ValuationDto>, IReportingRequest`
    - DTO: `ValuationDto(decimal CostValuation, decimal RetailValuation, decimal PotentialGrossValue)`
    - Handler: `GetValuationHandler` (injects `IReportingDbContext`; first production caller of `ValuationCalculator.Calculate(...)` from P-205)
- DTO:
  - `InventoryLineDto(Guid ProductId, Guid BranchId, string ProductName, string Category, int StockQty, decimal CostPrice, decimal EffectiveSellPrice, decimal GstRate, bool IsMarginScheme, string? HsnCode, string StockStatus)`
- API Controller (`StoreOS.Api.Controllers.InventoryController`):
  - `GET /api/inventory` -> `List([FromQuery] GetInventoryQuery query)`
  - `GET /api/inventory/low-stock` -> `LowStock([FromQuery] GetLowStockQuery query)`
  - `GET /api/inventory/valuation` -> `Valuation([FromQuery] GetValuationQuery query)`
- Infrastructure:
  - First production caller of `ReportingDbContext` via `IReportingRequest` with RepeatableRead and `transaction_read_only = on`.

## P-405

### Inventory Movements Append-Only Ledger Entity & Archetype B Policy
- Movement Types (`StoreOS.Domain.Inventory.MovementType`):
  ```csharp
  public static class MovementType
  {
      public const string Purchase = "Purchase";
      public const string Sale = "Sale";
      public const string SaleVoid = "SaleVoid";
      public const string SaleRefund = "SaleRefund";
      public const string SupplierReturn = "SupplierReturn";
      public const string RepairConsumed = "RepairConsumed";
      public const string RepairPartReturned = "RepairPartReturned";
      public const string Adjustment = "Adjustment";
      public const string TransferOut = "TransferOut";
      public const string TransferIn = "TransferIn";
      public static readonly IReadOnlyList<string> All = new[]
      {
          Purchase, Sale, SaleVoid, SaleRefund, SupplierReturn, RepairConsumed,
          RepairPartReturned, Adjustment, TransferOut, TransferIn
      };
  }
  ```
- Domain Entity (`StoreOS.Domain.Inventory.InventoryMovement`):
  ```csharp
  public class InventoryMovement
  {
      public long Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid BranchId { get; private set; }
      public Guid ProductId { get; private set; }
      public Guid? ImeiId { get; private set; }
      public string MovementType { get; private set; }
      public int QtyDelta { get; private set; }
      public decimal UnitCost { get; private set; }
      public string RefType { get; private set; }
      public string RefId { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }
      public Guid? CreatedBy { get; private set; }

      public static InventoryMovement Create(Guid tenantId, Guid branchId, Guid productId, Guid? imeiId,
          string movementType, int qtyDelta, decimal unitCost, string refType, string refId, Guid? createdBy);
  }
  ```
- Append-Only Invariant:
  - The `inventory_movements` table is strictly append-only. No UPDATE or DELETE operations are permitted at any layer. Every future stock mutation inserts a new row.
  - `qty_delta <> 0` is strictly enforced at both domain level and database level (`ck_inventory_movements_qty_delta`).
- Database Schema & Archetype B RLS:
  - Table: `public.inventory_movements`
    - Columns: `id` bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, `tenant_id` uuid NOT NULL, `branch_id` uuid NOT NULL REFERENCES branches(id), `product_id` uuid NOT NULL REFERENCES products(id), `imei_id` uuid NULL, `movement_type` text NOT NULL, `qty_delta` int NOT NULL, `unit_cost` numeric(18,2) NOT NULL, `ref_type` text NOT NULL, `ref_id` text NOT NULL, `created_at` timestamptz NOT NULL DEFAULT now(), `created_by` uuid NULL.
    - Constraints: `ck_inventory_movements_qty_delta` CHECK (`qty_delta <> 0`), `ix_movements_tenant_branch_product` on `(tenant_id, branch_id, product_id)`.
    - RLS Policy (`tenant_branch_isolation_policy`): Archetype B enabled and forced.
- Migration:
  - `20261005182800_011_InventoryMovements` applied via `dotnet ef migrations add 011_InventoryMovements --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`.

## P-406

### IInventoryStockService: Conditional Decrement / Increment Contract
- Service Interface (`StoreOS.Application.Inventory.IInventoryStockService`):
  ```csharp
  public interface IInventoryStockService
  {
      Task ReserveAndDecrementStockAsync(Guid branchId, Guid productId, int qty, string movementType,
          string refType, string refId, CancellationToken ct);

      Task IncreaseStockAsync(Guid branchId, Guid productId, int qty, decimal? newCostPrice,
          string movementType, string refType, string refId, CancellationToken ct);
  }
  ```
- Service Implementation (`StoreOS.Infrastructure.Inventory.InventoryStockService`):
  - Registered scoped in DI (`builder.Services.AddScoped<IInventoryStockService, InventoryStockService>()`).
  - **Hard Invariant**: This conditional `UPDATE branch_inventory SET stock_qty = stock_qty - @qty WHERE tenant_id = @tenantId AND branch_id = @branchId AND product_id = @productId AND stock_qty >= @qty` pattern via `ExecuteSqlInterpolatedAsync` is the **ONLY** sanctioned way to decrement branch stock anywhere in the codebase.
  - Every subsequent prompt modifying branch stock (P-410, P-510, P-524, P-525, P-608, P-705, P-414, P-413) MUST invoke `IInventoryStockService`.
  - Neither method calls `SaveChangesAsync()`; operations are staged on the ambient `StoreOsDbContext` so callers commit atomically along with sale lines, audit logs, and payments.
- Concurrency Verification:
  - Verified via `StoreOS.ConcurrencyTests/InventoryStockConcurrencyTests.cs` that two simultaneous concurrent tasks decrementing stock of 1 produce exactly one successful winner and one `insufficient_stock` domain exception, with zero negative stock or double-decrement.

## P-407

### Inventory IMEIs Entity, Migration & Tenant-Wide Active-Unique Index
- Statuses (`StoreOS.Domain.Inventory.ImeiStatus`):
  ```csharp
  public static class ImeiStatus
  {
      public const string InStock = "InStock";
      public const string Sold = "Sold";
      public const string ReturnedToSupplier = "ReturnedToSupplier";
      public const string InTransit = "InTransit";
      public static readonly IReadOnlyList<string> All = new[] { InStock, Sold, ReturnedToSupplier, InTransit };
  }
  ```
- Domain Entity (`StoreOS.Domain.Inventory.InventoryImei`):
  ```csharp
  public class InventoryImei
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid BranchId { get; private set; }
      public Guid ProductId { get; private set; }
      public Guid? PurchaseId { get; private set; }
      public string Imei { get; private set; }
      public string Status { get; private set; }
      public Guid? SaleId { get; private set; }
      public Guid? SaleItemId { get; private set; }
      public Guid? ReturnId { get; private set; }
      public DateOnly? PurchaseDate { get; private set; }
      public decimal? PurchasePrice { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }

      public static InventoryImei Create(Guid tenantId, Guid branchId, Guid productId, Guid? purchaseId,
          string imei, DateOnly purchaseDate, decimal purchasePrice);
      public void MarkSold(Guid saleId, Guid saleItemId);
      public void RestoreToStock();
      public void MarkReturnedToSupplier(Guid returnId);
      public void MarkInTransit();
      public void CompleteTransferIn(Guid newBranchId);
  }
  ```
- Tenant-Wide Active-Unique Index & Constraints:
  - Partial unique index:
    ```sql
    CREATE UNIQUE INDEX ux_imei_active ON public.inventory_imeis (tenant_id, imei)
      WHERE status IN ('InStock','Sold','InTransit');
    ```
  - Lookup index: `ix_imei_lookup` on `(tenant_id, imei)`.
  - CHECK constraint: `ck_inventory_imeis_status` CHECK (`status IN ('InStock','Sold','ReturnedToSupplier','InTransit')`).
  - Invariant 1 Enforcement: Exactly one active physical device may exist per `(tenant_id, imei)` regardless of branch. Returned units are excluded, permitting valid re-stocking.
  - RLS Policy: Archetype B branch-scoped isolation policy (`tenant_branch_isolation_policy`) enabled and forced.
- Migration:
  - `20261005183636_012_InventoryImeis` applied via:
    `dotnet ef migrations add 012_InventoryImeis --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-408

### IInventoryStockService: SellImeisAsync & Status Transitions
- DTO (`StoreOS.Application.Inventory.InventoryImeiSnapshot`):
  ```csharp
  public record InventoryImeiSnapshot(Guid Id, Guid ProductId, Guid BranchId, decimal? PurchasePrice, string Status);
  ```
- Extended Interface (`StoreOS.Application.Inventory.IInventoryStockService`):
  ```csharp
  public interface IInventoryStockService
  {
      // (P-406 methods)
      Task ReserveAndDecrementStockAsync(Guid branchId, Guid productId, int qty, string movementType,
          string refType, string refId, CancellationToken ct);
      Task IncreaseStockAsync(Guid branchId, Guid productId, int qty, decimal? newCostPrice,
          string movementType, string refType, string refId, CancellationToken ct);

      // (P-408 extensions)
      Task SellImeiAsync(Guid imeiRowId, Guid saleId, Guid saleItemId, CancellationToken ct);
      Task RestoreImeiToStockAsync(Guid imeiRowId, CancellationToken ct);
      Task<InventoryImeiSnapshot> LoadImeiForSaleAsync(Guid branchId, string imei, CancellationToken ct);
  }
  ```
- Concurrency & Double-Sale Prevention:
  - `SellImeiAsync` executes `SELECT * FROM inventory_imeis WHERE id = @id AND tenant_id = @tenantId FOR UPDATE` row locking.
  - Empirically observed failure mode: in RepeatableRead isolation, when two concurrent requests execute `SellImeiAsync` for the same row, the second transaction is blocked by Postgres row-level lock and subsequently receives `PostgresException` (SqlState 40001: could not serialize access due to concurrent update). If the first transaction committed before the second started its row lock, `MarkSold` throws `DomainValidationException("imei_not_available")`. In both paths, exactly one sale commits and double-sale is mathematically impossible.

## P-409

### Purchases Entity & Migration
- Domain Entity (`StoreOS.Domain.Purchasing.Purchase`):
  ```csharp
  public class Purchase
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid BranchId { get; private set; }
      public Guid ProductId { get; private set; }
      public int Qty { get; private set; }
      public decimal PurchaseRate { get; private set; }
      public Guid? SupplierId { get; private set; }
      public string? SupplierName { get; private set; }
      public string? SupplierPhone { get; private set; }
      public string? SupplierAddress { get; private set; }
      public string? SellerName { get; private set; }
      public string? SellerPhone { get; private set; }
      public string? SellerAddress { get; private set; }
      public string? SupplierInvoiceNo { get; private set; }
      public Guid? ImportBatchId { get; private set; }
      public DateOnly PurchaseDate { get; private set; }
      public Guid CreatedBy { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }

      public static Purchase Create(Guid tenantId, Guid branchId, Guid productId, int qty,
          decimal purchaseRate, Guid? supplierId, string? supplierName, string? supplierPhone,
          string? supplierAddress, DateOnly purchaseDate, Guid createdBy,
          string? supplierInvoiceNo = null, Guid? importBatchId = null,
          string? sellerName = null, string? sellerPhone = null, string? sellerAddress = null);
  }
  ```
- Database Schema & Invariants:
  - Table: `public.purchases`
    - Columns: `id` uuid PK, `tenant_id` uuid NOT NULL, `branch_id` uuid NOT NULL REFERENCES branches(id), `product_id` uuid NOT NULL REFERENCES products(id), `qty` int NOT NULL, `purchase_rate` numeric(18,2) NOT NULL, `supplier_id` uuid NULL, `supplier_name` text, `supplier_phone` text, `supplier_address` text, `seller_name` text, `seller_phone` text, `seller_address` text, `supplier_invoice_no` text, `import_batch_id` uuid, `purchase_date` date NOT NULL, `created_by` uuid NOT NULL, `created_at` timestamptz NOT NULL DEFAULT now().
    - Constraints: `ck_purchases_qty` CHECK (`qty > 0`).
    - Indexes: `ix_purchases_tenant_branch_product` on `(tenant_id, branch_id, product_id)`, `ix_purchases_tenant_branch_date` on `(tenant_id, branch_id, purchase_date)`.
    - Foreign Keys: `supplier_id` is a bare nullable column in this migration; foreign-key constraint to `suppliers(id)` is deferred to P-411.
    - RLS Policy: Archetype B branch-scoped isolation policy (`tenant_branch_isolation_policy`) enabled and forced.
- Migration:
  - `20261005184415_013_Purchases` applied via:
    `dotnet ef migrations add 013_Purchases --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-410

### Stock-In Endpoint with Optional IMEIs
- Command & Result (`StoreOS.Application.Purchasing.StockIn`):
  ```csharp
  public record StockInCommand(Guid ProductId, int Qty, decimal PurchaseRate,
      Guid? SupplierId, string? SupplierName, string? SupplierPhone, string? SupplierAddress,
      DateOnly PurchaseDate, string? SupplierInvoiceNo, IReadOnlyList<string>? Imeis)
      : IRequest<StockInResult>, IRequiresPermission
  {
      public string RequiredPermission => "purchases:manage";
  }

  public record StockInResult(Guid PurchaseId, int NewStockQty, IReadOnlyList<Guid> CreatedImeiIds);
  ```
- Validator (`StoreOS.Application.Purchasing.StockIn.StockInValidator`):
  - Validates `Qty > 0`, `PurchaseRate >= 0`.
  - If `Imeis` is provided, enforces `Imeis.Count == Qty` exactly.
  - Rejects empty and duplicate IMEIs submitted in the same request payload.
- Query & DTO (`StoreOS.Application.Purchasing.GetPurchases`):
  - `GetPurchasesQuery(Guid? ProductId = null, Guid? BranchId = null, DateOnly? FromDate = null, DateOnly? ToDate = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<PurchaseDto>>, IReadOnlyRequest`
  - `PurchaseDto(Guid Id, Guid BranchId, Guid ProductId, int Qty, decimal PurchaseRate, Guid? SupplierId, string? SupplierName, string? SupplierPhone, string? SupplierAddress, string? SellerName, string? SellerPhone, string? SellerAddress, string? SupplierInvoiceNo, DateOnly PurchaseDate, Guid CreatedBy, DateTimeOffset CreatedAt)`
- Controller (`StoreOS.Api.Controllers.PurchasesController`):
  - `POST /api/purchases` -> `StockIn([FromBody] StockInCommand command)`
  - `GET /api/purchases` -> `List([FromQuery] GetPurchasesQuery query)`
- Exception Translation:
  - Intercepts Postgres `23505` unique-violation on `ux_imei_active` and throws `DomainException("imei_already_active", ...)`, mapped to HTTP 409 Conflict by `ErrorCatalog`.

## P-411

### Suppliers Minimal Master Entity & FK Retrofit
- Domain Entity (`StoreOS.Domain.Suppliers.Supplier`):
  ```csharp
  public class Supplier
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public string Name { get; private set; }
      public string? Phone { get; private set; }
      public string? Address { get; private set; }
      public string? BrandOrCategory { get; private set; }
      public string? Notes { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }

      public static Supplier Create(Guid tenantId, string name, string? phone, string? address,
          string? brandOrCategory, string? notes);
      public void UpdateProfile(string name, string? phone, string? address, string? brandOrCategory, string? notes);
  }
  ```
- Vertical Slices (`StoreOS.Application.Suppliers`):
  - `CreateSupplierCommand(string Name, string? Phone, string? Address, string? BrandOrCategory, string? Notes) : IRequest<Guid>, IRequiresPermission` (`purchases:manage`)
  - `GetSuppliersQuery(string? Search, int Page = 1, int PageSize = 50) : IRequest<PagedResult<SupplierDto>>, IReadOnlyRequest, IRequiresPermission` (`purchases:manage`)
  - `GetSupplierByIdQuery(Guid Id) : IRequest<SupplierDto>, IReadOnlyRequest, IRequiresPermission` (`purchases:manage`)
  - `UpdateSupplierCommand(Guid Id, string Name, string? Phone, string? Address, string? BrandOrCategory, string? Notes) : IRequest, IRequiresPermission` (`purchases:manage`)
  - DTO: `SupplierDto(Guid Id, string Name, string? Phone, string? Address, string? BrandOrCategory, string? Notes, DateTimeOffset CreatedAt)`
- Controller (`StoreOS.Api.Controllers.SuppliersController`):
  - `GET /api/suppliers`
  - `GET /api/suppliers/{id:guid}`
  - `POST /api/suppliers`
  - `PUT /api/suppliers/{id:guid}`
- Database Schema & Invariants:
  - Table: `public.suppliers` (`id` uuid PK, `tenant_id` uuid NOT NULL, `name` text NOT NULL, `phone` text, `address` text, `brand_or_category` text, `notes` text, `created_at` timestamptz DEFAULT now()).
  - Index: `ix_suppliers_tenant_name` on `(tenant_id, name)`.
  - RLS Policy: Tenant-only RLS policy (`tenant_isolation_policy`).
  - Foreign Key Retrofit: `ALTER TABLE purchases ADD CONSTRAINT fk_purchases_supplier FOREIGN KEY (supplier_id) REFERENCES suppliers(id);`.
- Migration:
  - `20261005185105_014_SuppliersMaster` applied via:
    `dotnet ef migrations add 014_SuppliersMaster --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-412

### Supplier Invoice Import Pipeline (Deterministic & Deduped)
- Domain Entity (`StoreOS.Domain.Platform.ImportBatch`):
  ```csharp
  public static class ImportSourceType { public const string SupplierInvoice = "SupplierInvoice"; public const string Excel = "Excel"; public const string JsonBackup = "JsonBackup"; }
  public static class ImportStatus { public const string Pending = "Pending"; public const string Imported = "Imported"; public const string Failed = "Failed"; }
  public class ImportBatch
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid? BranchId { get; private set; }
      public string SourceType { get; private set; }
      public string SourceRef { get; private set; }
      public string Status { get; private set; }
      public string? RecordCounts { get; private set; }
      public Guid CreatedBy { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }
      public static ImportBatch Create(Guid tenantId, Guid? branchId, string sourceType, string sourceRef, Guid createdBy);
      public void MarkImported(string? recordCountsJson);
      public void MarkFailed(string? reasonJson);
      public void UpdateRecordCounts(string? recordCountsJson);
  }
  ```
- Import Pipeline Contracts (`StoreOS.Application.Purchasing.ImportSupplierInvoice`):
  ```csharp
  public record SupplierInvoiceImportLine(string ProductNameRaw, string? HsnCode, int Qty, decimal Rate, string? Imei);
  public record ImportSupplierInvoiceCommand(Guid SupplierId, string SupplierInvoiceNo, DateOnly InvoiceDate, string SourceRef, IReadOnlyList<SupplierInvoiceImportLine> Lines) : IRequest<ImportResult>, IRequiresPermission
  {
      public string RequiredPermission => "purchases:manage";
  }
  public record ImportResult(Guid ImportBatchId, string Status, int ProductsMatched, int ProductsAmbiguous, IReadOnlyList<AmbiguousLine> AmbiguousLines, int PurchasesCreated);
  public record AmbiguousLine(string ProductNameRaw, IReadOnlyList<Guid> CandidateProductIds);
  ```
- Matching Rules:
  - **Deterministic Rule**: Strict case-insensitive string equality on `products.name` ONLY. No fuzzy, Levenshtein, or AI/ML matching is permitted.
  - Unambiguous lines are processed immediately via `StockInCommand`.
  - 0 matches or >1 matches are added to `AmbiguousLines` (candidate IDs provided for >1 matches; empty list for 0 matches), batch status set to `Pending`.
  - Deduplication: When `(tenant_id, source_ref)` exists with status `Imported`, the cached `ImportResult` is returned without reprocessing or creating duplicate stock.
- Controller:
  - `POST /api/imports/supplier-invoice` (`StoreOS.Api.Controllers.ImportsController`)
- Migration:
  - `20261005185632_015_ImportBatches` applied via:
    `dotnet ef migrations add 015_ImportBatches --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-413

### Inventory Adjustments Endpoint (Live-DB Gated)
- Command & Result (`StoreOS.Application.Inventory.Adjustments`):
  ```csharp
  public record AdjustStockCommand(Guid ProductId, int QtyDelta, string Reason)
      : IRequest<AdjustStockResult>, IHighImpactOperation
  {
      public string RequiredPermission => "inventory:adjustments";
  }

  public record AdjustStockResult(int NewStockQty);
  ```
- Validator (`StoreOS.Application.Inventory.Adjustments.AdjustStockValidator`):
  - Enforces `QtyDelta != 0` and mandatory non-empty `Reason`.
- Handler & Audit Behavior:
  - Positive delta calls `IInventoryStockService.IncreaseStockAsync` with `MovementType.Adjustment`.
  - Negative delta calls `IInventoryStockService.ReserveAndDecrementStockAsync` with `-QtyDelta` and `MovementType.Adjustment`.
  - Stock can never go negative (throws `insufficient_stock`).
  - Appends to `IAuditWriter` action `"inventory.adjusted"` preserving the `Reason` string permanently in the audit trail.
  - Live DB check: implements `IHighImpactOperation`, bypassing memory cache to guarantee immediate authorization enforcement.
- Controller:
  - `POST /api/inventory/adjustments` (`StoreOS.Api.Controllers.InventoryController`)

## P-414

### Branch Transfers Out/In Endpoints & Archetype C Policy
- Domain Entity (`StoreOS.Domain.Inventory.InventoryTransfer`):
  ```csharp
  public static class TransferStatus { public const string Pending = "Pending"; public const string Received = "Received"; }
  public class InventoryTransfer
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid FromBranchId { get; private set; }
      public Guid ToBranchId { get; private set; }
      public Guid ProductId { get; private set; }
      public int Qty { get; private set; }
      public string Status { get; private set; }
      public Guid[]? ImeiIds { get; private set; }
      public Guid CreatedBy { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }
      public Guid? ReceivedBy { get; private set; }
      public DateTimeOffset? ReceivedAt { get; private set; }
      public static InventoryTransfer CreatePending(Guid tenantId, Guid fromBranchId, Guid toBranchId, Guid productId, int qty, Guid[]? imeiIds, Guid createdBy);
      public void MarkReceived(Guid receivedBy);
  }
  ```
- Vertical Slices (`StoreOS.Application.Inventory.Transfers`):
  - `TransferOutCommand(Guid ToBranchId, Guid ProductId, int Qty, IReadOnlyList<string>? ImeiStrings = null) : IRequest<Guid>, IRequiresPermission` (`inventory:manage`)
  - `ReceiveTransferCommand(Guid TransferId) : IRequest, IRequiresPermission` (`inventory:manage`)
- Two-Step Workflow & Concurrency Rules:
  - Step 1: `TransferOut` validates `ToBranchId != CurrentBranchId`, sets IMEI rows to `InTransit`, decrements source branch stock via conditional decrement `ReserveAndDecrementStockAsync`, and creates header with `Status = Pending`.
  - Step 2: `ReceiveTransfer` verifies receiving staff branch matches `ToBranchId`, increases destination branch stock via `IncreaseStockAsync`, updates IMEIs to `InStock` at destination branch, and marks transfer `Received`.
  - InTransit IMEIs are unsellable at both origin and destination branches.
  - Double-receipt is rejected with `DomainValidationException("transfer_already_received")`.
  - Tenant-level total stock is invariant across the entire lifecycle.
- Archetype C — Dual-Branch Visibility Query Filter & RLS Policy:
  - Filter: `(s.AllBranches || s.BranchIds.Contains(it.FromBranchId) || s.BranchIds.Contains(it.ToBranchId))`
  - RLS Policy:
    ```sql
    CREATE POLICY tenant_branch_dual_isolation_policy ON public.inventory_transfers USING (
      tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
      AND (
        COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
        OR from_branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
        OR to_branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
      )
    );
    ```
- Controller:
  - `POST /api/inventory/transfers`
  - `POST /api/inventory/transfers/{id}/receive`
- Migration:
  - `20261005190340_016_Transfers` applied via:
    `dotnet ef migrations add 016_Transfers --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext && dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-501
### invoice_sequences Table & Atomic Upsert Allocator
- Application Interface (`StoreOS.Application.Abstractions.IInvoiceSequenceAllocator`):
  ```csharp
  namespace StoreOS.Application.Abstractions;

  public interface IInvoiceSequenceAllocator
  {
      Task<int> AllocateNextAsync(Guid tenantId, Guid branchId, string seriesKey, CancellationToken ct);
  }
  ```
- Domain Entity (`StoreOS.Domain.Sales.InvoiceSequence`):
  ```csharp
  namespace StoreOS.Domain.Sales;

  public class InvoiceSequence
  {
      public Guid TenantId { get; private set; }
      public Guid BranchId { get; private set; }
      public string SeriesKey { get; private set; } = default!;
      public int LastSeq { get; private set; }
      public static InvoiceSequence Create(Guid tenantId, Guid branchId, string seriesKey, int lastSeq = 0);
  }
  ```
- Infrastructure Implementation (`StoreOS.Infrastructure.Sales.InvoiceSequenceAllocator`):
  - Reuses ambient connection/transaction via `_db.Database.GetDbConnection()` and `_db.Database.CurrentTransaction?.GetDbTransaction()`.
  - Atomic upsert query:
    ```sql
    INSERT INTO invoice_sequences (tenant_id, branch_id, series_key, last_seq)
    VALUES (@t, @b, @s, 1)
    ON CONFLICT (tenant_id, branch_id, series_key)
    DO UPDATE SET last_seq = invoice_sequences.last_seq + 1
    RETURNING last_seq;
    ```
  - Registered scoped: `AddScoped<IInvoiceSequenceAllocator, InvoiceSequenceAllocator>()`.
- Table Schema & RLS Policy:
  - Table: `public.invoice_sequences` with composite PK `(tenant_id, branch_id, series_key)`.
  - Branch-scoped RLS policy (`tenant_branch_isolation_policy`):
    ```sql
    ALTER TABLE public.invoice_sequences ENABLE ROW LEVEL SECURITY;
    ALTER TABLE public.invoice_sequences FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_branch_isolation_policy ON public.invoice_sequences USING (
      tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
      AND (
        COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
        OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
      )
    );
    ```
- Empirical Findings on Rollback:
  - Allocations participate directly in the ambient database transaction.
  - When a transaction containing an `AllocateNextAsync` call is rolled back (`ROLLBACK`), the row insert/update in `invoice_sequences` is rolled back completely by PostgreSQL. A subsequent allocation in a new transaction returns the exact same number, introducing zero gap.
- Concurrency Verification:
  - 50 concurrent transactions allocating for the same `(tenant_id, branch_id, series_key)` produce strictly unique integers `1..50` with no gaps and no duplicate allocations.
- Migration:
  - `20261005193020_017_InvoiceSequences` applied via:
    `dotnet ef migrations add 017_InvoiceSequences --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
    `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-502
### Sales & Sale Items Core Transactional Entities
- Domain Constants:
  - `StoreOS.Domain.Sales.InvoiceType`: `TaxInvoice = "TaxInvoice"`, `Estimate = "Estimate"`, `All`
  - `StoreOS.Domain.Sales.SaleStatus`: `Active = "Active"`, `Voided = "Voided"`, `Refunded = "Refunded"`, `All`
  - `StoreOS.Domain.Sales.PaymentMode`: `Cash = "Cash"`, `UPI = "UPI"`, `Card = "Card"`, `Credit = "Credit"`, `Finance = "Finance"`, `All`
- Domain Entities:
  - `StoreOS.Domain.Sales.SaleItem`:
    ```csharp
    public class SaleItem
    {
        public Guid Id { get; private set; }
        public Guid TenantId { get; private set; }
        public Guid SaleId { get; private set; }
        public Guid? ProductId { get; private set; }
        public string ItemName { get; private set; }
        public string Category { get; private set; }
        public int Qty { get; private set; }
        public decimal PricePerUnit { get; private set; }
        public decimal? PurchasePriceSnapshot { get; private set; }
        public string? HsnSnapshot { get; private set; }
        public bool IsMarginScheme { get; private set; }
        public bool IsGift { get; private set; }
        public decimal Taxable { get; private set; }
        public decimal Cgst { get; private set; }
        public decimal Sgst { get; private set; }
        public string? Imei { get; private set; }
        public static SaleItem Create(...);
    }
    ```
    - **Snapshot Immutability Rule:** `item_name`, `category`, `hsn_snapshot`, and `purchase_price_snapshot` are written once at creation and never mutated afterward by any later prompt, maintaining historical accuracy even if underlying products change.
  - `StoreOS.Domain.Sales.Sale`:
    ```csharp
    public class Sale
    {
        public Guid Id { get; private set; }
        public Guid TenantId { get; private set; }
        public Guid BranchId { get; private set; }
        public string InvoiceNumber { get; private set; }
        public string InvoiceType { get; private set; }
        public DateTimeOffset SaleDate { get; private set; }
        public Guid? CustomerId { get; private set; }
        public string? CustomerName { get; private set; }
        public string? CustomerGstin { get; private set; }
        public decimal TotalTaxable { get; private set; }
        public decimal TotalCgst { get; private set; }
        public decimal TotalSgst { get; private set; }
        public decimal GrandTotal { get; private set; }
        public decimal Discount { get; private set; }
        public decimal AmountPaid { get; private set; }
        public string Status { get; private set; }
        public string? PaymentMode { get; private set; }
        public string? FinanceProvider { get; private set; }
        public string? FinancerName { get; private set; }
        public decimal? DownPayment { get; private set; }
        public string? EmiDetailsJson { get; private set; }
        public string? GiftDetailsJson { get; private set; }
        public string? Description { get; private set; }
        public Guid? RepairJobId { get; private set; }
        public string? Irn { get; private set; }
        public string? IrnAckNo { get; private set; }
        public string? IrnQrData { get; private set; }
        public Guid CreatedBy { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public IReadOnlyList<SaleItem> Items { get; }
        public static Sale CreateTaxInvoice(...);
        public static Sale CreateEstimate(...);
        public void AddItem(SaleItem item);
        public void MarkVoided();
        public void MarkRefunded();
        public void ApplyPayment(decimal amount);
        public void SetIrn(string irn, string irnAckNo, string irnQrData);
    }
    ```
- Database Tables & Constraints:
  - Table `public.sales`: PK `id`, FK `branch_id -> branches(id)`, `UNIQUE(tenant_id, invoice_number)`.
    - Indexes: `ix_sales_tenant_branch_date (tenant_id, branch_id, sale_date)`, `ix_sales_tenant_status (tenant_id, status)`, partial `ix_sales_tenant_irn (tenant_id, irn) WHERE irn IS NOT NULL`.
    - Check constraints: `ck_sales_invoice_type (TaxInvoice, Estimate)`, `ck_sales_status (Active, Voided, Refunded)`, `ck_sales_payment_mode (Cash, UPI, Card, Credit, Finance)`.
    - Archetype B RLS: `tenant_branch_isolation_policy`.
  - Table `public.sale_items`: PK `id`, FK `sale_id -> sales(id) ON DELETE CASCADE`, FK `product_id -> products(id) ON DELETE RESTRICT`.
    - Indexes: `ix_sale_items_sale (tenant_id, sale_id)`, `ix_sale_items_imei (tenant_id, imei)`.
    - Check constraint: `ck_sale_items_qty (qty > 0)`.
    - Archetype A RLS: `tenant_isolation_policy` on `tenant_id`.
- Migration:
  - `20261005193528_018_Sales` applied via:
    `dotnet ef migrations add 018_Sales --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
    `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-503
### POS Product Search & Single Product Detail Endpoints
- Application Queries & DTOs (`StoreOS.Application.Sales.Search`):
  - `PosSearchQuery(string Q) : IRequest<IReadOnlyList<PosSearchResultDto>>, IReadOnlyRequest`
  - `PosSearchResultDto(Guid ProductId, string Name, string Category, decimal SellPrice, decimal GstRate, string? HsnCode, bool IsMarginScheme, int StockQty, string StockStatus)`
  - `GetPosProductQuery(Guid Id) : IRequest<PosProductDetailDto>, IReadOnlyRequest`
  - `PosProductDetailDto(Guid ProductId, string Name, string Category, decimal SellPrice, decimal GstRate, string? HsnCode, bool IsMarginScheme, int StockQty, string StockStatus, IReadOnlyList<string> AvailableImeis)`
- Handlers:
  - `PosSearchHandler`: searches products matching query case-insensitively by name, plus exact IMEI match for 14-16 digit numbers via `inventory_imeis` join. Computes branch-scoped stock qty and stock status (`InStock`, `LowStock`, `OutOfStock`). For `RepairService` products, always returns `StockQty = 0` and `StockStatus = "N/A"`.
  - `GetPosProductHandler`: returns detailed product info with branch-scoped stock qty, status, and sorted list of available `InStock` IMEIs at current branch.
- Endpoints (`StoreOS.Api.Controllers.PosController`):
  - `GET /api/pos/search?q={query}`
  - `GET /api/pos/product/{id:guid}`

## P-504
### CheckoutCommand & Full Structural Request Shape Validation
- Application Commands & DTOs (`StoreOS.Application.Sales.Checkout`):
  ```csharp
  public record CheckoutCartLine(
      Guid ProductId,
      int Qty,
      string? Imei,
      bool UseMarginScheme,
      bool IsGift,
      decimal? OverridePricePerUnit);

  public record CheckoutPayment(
      string PaymentMode,
      decimal Amount);

  public record CheckoutFinanceDetails(
      string FinanceProvider,
      string? FinancerName,
      decimal DownPayment,
      int TenureMonths,
      decimal AnnualInterestRate);

  public record CheckoutCommand(
      IReadOnlyList<CheckoutCartLine> Lines,
      Guid? CustomerId,
      string? WalkInCustomerName,
      decimal InvoiceDiscount,
      string InvoiceType,
      IReadOnlyList<CheckoutPayment> Payments,
      CheckoutFinanceDetails? Finance,
      string? Description
  ) : IRequest<CheckoutResult>, IRequiresPermission
  {
      public string RequiredPermission => "pos:checkout";
  }

  public record CheckoutResult(
      Guid SaleId,
      string InvoiceNumber,
      decimal GrandTotal,
      decimal AmountPaid,
      decimal Balance,
      DateTimeOffset SaleDate);
  ```
- Validators (`StoreOS.Application.Sales.Checkout`):
  - `CheckoutValidator : AbstractValidator<CheckoutCommand>`: enforces non-empty cart, line `Qty > 0`, non-negative line override price, non-negative discount, valid invoice type (`TaxInvoice` or `Estimate`), non-empty payments collection with positive amounts and valid payment modes (`Cash`, `UPI`, `Card`, `Credit`, `Finance`), optional finance details validation.
  - `CheckoutFinanceDetailsValidator : AbstractValidator<CheckoutFinanceDetails>`: enforces non-empty finance provider, non-negative down payment, `TenureMonths > 0`, non-negative annual interest rate.

## P-505
### Server-Side Pricing Resolution & Margin Scheme Cost Cascade
- Application Domain Records (`StoreOS.Application.Sales.Checkout`):
  ```csharp
  public record ResolvedCartLine(
      Guid ProductId,
      string ProductName,
      string Category,
      int Qty,
      decimal PricePerUnit,
      decimal? PurchasePriceSnapshot,
      string? HsnCode,
      decimal GstRate,
      bool IsMarginScheme,
      bool IsGift,
      string? Imei,
      Guid? ImeiRowId,
      decimal LineTaxable,
      decimal LineCgst,
      decimal LineSgst);
  ```
- Pricing Resolver Service (`StoreOS.Application.Sales.Checkout.PricingResolver`):
  - Registered scoped: `AddScoped<PricingResolver>()`.
  - `Task<ResolvedCartLine> ResolveAsync(Guid branchId, CheckoutCartLine line, CancellationToken ct)`
  - **Price Resolution:**
    - Base sell price resolves from `branch_inventory.sell_price_override` if set, else `products.sell_price`.
    - If `line.OverridePricePerUnit` is provided, it is applied directly.
  - **Fixed Cost-Basis Cascade Order (Invariant):**
    1. IMEI row's own `purchase_price` (loaded via `IInventoryStockService.LoadImeiForSaleAsync`) when `line.Imei` is specified.
    2. Branch inventory's `cost_price` if `> 0` when margin scheme is requested without an IMEI.
    3. Product master's `purchase_price` as the final fallback.
    - Throws `margin_scheme_invalid_cost_basis` (422) if cost basis $\le 0$ when margin scheme is evaluated.
    - Throws `margin_scheme_invalid_category` (422) if margin scheme is requested on any category other than `UsedPhone`.
  - **Gift & Tax Rules:**
    - Gift lines resolve base pricing for informational and stock purposes, but assign `LineTax.Zero` (`LineTaxable = 0`, `LineCgst = 0`, `LineSgst = 0`).
    - Non-gift lines compute standard GST or margin-scheme GST via `TaxEngine`.

## P-506
### Minimal Customer Master & Customer Ledger Entities
- Domain Entities (`StoreOS.Domain.Customers`):
  - `Customer`:
    ```csharp
    public class Customer
    {
        public Guid Id { get; private set; }
        public Guid TenantId { get; private set; }
        public string Name { get; private set; }
        public string? Phone { get; private set; }
        public string? Gstin { get; private set; }
        public decimal TotalPurchases { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public static Customer Create(Guid tenantId, string name, string? phone, string? gstin);
        public void UpdateProfile(string name, string? phone, string? gstin);
        public void AddToTotalPurchases(decimal amount);
        public void SubtractFromTotalPurchases(decimal amount);
    }
    ```
  - `CustomerLedgerEntryType`: `Invoice = "Invoice"`, `Payment = "Payment"`, `InvoiceVoid = "InvoiceVoid"`, `Refund = "Refund"`, `OpeningBalance = "OpeningBalance"`, `All`
  - `CustomerLedgerEntry`:
    ```csharp
    public class CustomerLedgerEntry
    {
        public long Id { get; private set; }
        public Guid TenantId { get; private set; }
        public Guid CustomerId { get; private set; }
        public Guid? SaleId { get; private set; }
        public string EntryType { get; private set; }
        public decimal Debit { get; private set; }
        public decimal Credit { get; private set; }
        public Guid? BranchId { get; private set; }
        public DateOnly EntryDate { get; private set; }
        public string? RefId { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
        public static CustomerLedgerEntry CreateDebit(...);
        public static CustomerLedgerEntry CreateCredit(...);
    }
    ```
- Database Tables & Constraints:
  - `public.customers`: PK `id`, `name text NOT NULL`, `phone text`, `gstin text`, `total_purchases numeric(18,2) DEFAULT 0`, `created_at timestamptz DEFAULT now()`. Index: `ix_customers_tenant_phone (tenant_id, phone)`. Archetype A RLS: `tenant_isolation_policy` on `tenant_id`.
  - `public.customer_ledger_entries`: PK `id bigint GENERATED ALWAYS AS IDENTITY`, FK `customer_id -> customers(id) RESTRICT`, FK `sale_id -> sales(id) RESTRICT`, FK `branch_id -> branches(id) RESTRICT`. Index: `ix_custledger_customer (tenant_id, customer_id)`. Archetype A RLS: `tenant_isolation_policy` on `tenant_id`.
- Two Query Patterns (Critical Architecture Rule):
  1. **Branch-Scoped Detail View:** Queries run through EF Core's standard global query filter `cle => cle.TenantId == s.TenantId && (cle.BranchId == null || s.AllBranches || s.BranchIds.Contains(cle.BranchId.Value))` so branch-scoped cashiers only see ledger rows stamped for their branch (or tenant-unscoped entries).
  2. **Tenant-Global Balance Calculation (Documented Exception):** Per Requirements §17.5, customer credit is tenant-global. Balance aggregation queries explicitly use `.IgnoreQueryFilters()` filtered strictly by `tenant_id` (and `customer_id`) to sum debits and credits across all branches.
- Migration:
  - `20261005194733_019_CustomersLedger` applied via:
    `dotnet ef migrations add 019_CustomersLedger --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
    `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-507
### ICustomerLedgerService & Cross-Branch Consolidated Balance
- Application Contract (`StoreOS.Application.Customers.ICustomerLedgerService`):
  ```csharp
  namespace StoreOS.Application.Customers;

  public interface ICustomerLedgerService
  {
      Task RecordInvoiceDebitAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
          DateOnly entryDate, CancellationToken ct);
      Task RecordPaymentCreditAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
          DateOnly entryDate, CancellationToken ct);
      Task RecordVoidReversalAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
          DateOnly entryDate, CancellationToken ct);
      Task RecordRefundCreditAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
          DateOnly entryDate, CancellationToken ct);
      Task<decimal> GetConsolidatedBalanceAsync(Guid customerId, CancellationToken ct);
  }
  ```
- Infrastructure Implementation (`StoreOS.Infrastructure.Customers.CustomerLedgerService`):
  - Registered scoped: `AddScoped<ICustomerLedgerService, CustomerLedgerService>()`.
  - `RecordInvoiceDebitAsync`: stages a debit row with `CustomerLedgerEntryType.Invoice`.
  - `RecordPaymentCreditAsync`: stages a credit row with `CustomerLedgerEntryType.Payment`.
  - `RecordVoidReversalAsync`: stages a credit row with `CustomerLedgerEntryType.InvoiceVoid`.
  - `RecordRefundCreditAsync`: stages a credit row with `CustomerLedgerEntryType.Refund`.
  - Staged-write convention: none of the `Record*Async` methods call `SaveChangesAsync()`; atomic commit is owned by the calling request pipeline handler.
  - `GetConsolidatedBalanceAsync`: the sole sanctioned balance-computation path in the entire codebase (P-509, P-601, P-905). Explicitly uses `.IgnoreQueryFilters()` with `e.TenantId == _scope.TenantId && e.CustomerId == customerId` to sum debits and credits across all branches, returning `Math.Max(0m, sums.Debit - sums.Credit)`.

## P-508
### customer_payments Entity & Payment Composition Helper
- Domain Entity (`StoreOS.Domain.Sales.CustomerPayment`):
  ```csharp
  namespace StoreOS.Domain.Sales;

  public class CustomerPayment
  {
      public Guid Id { get; private set; }
      public Guid TenantId { get; private set; }
      public Guid SaleId { get; private set; }
      public decimal Amount { get; private set; }
      public string PaymentMode { get; private set; }
      public DateOnly PaymentDate { get; private set; }
      public Guid ReceivedBy { get; private set; }
      public DateTimeOffset CreatedAt { get; private set; }
      public static CustomerPayment Create(Guid tenantId, Guid saleId, decimal amount,
          string paymentMode, DateOnly paymentDate, Guid receivedBy);
  }
  ```
  - **Permanent Immutability Rule (Invariant 5):** `CustomerPayment` exposes NO public mutation methods whatsoever. A payment transaction is an immutable record; corrections are modeled as new records, never updates.
- Pure Composition Helper (`StoreOS.Application.Sales.Checkout.PaymentComposer`):
  ```csharp
  namespace StoreOS.Application.Sales.Checkout;

  public static class PaymentComposer
  {
      public static (IReadOnlyList<CustomerPayment> Payments, decimal TotalPaid) Compose(
          Guid tenantId, Guid saleId, IReadOnlyList<CheckoutPayment> requested, DateOnly paymentDate, Guid receivedBy);
  }
  ```
- Database Tables & Constraints:
  - Table `public.customer_payments`: PK `id`, FK `sale_id -> sales(id) RESTRICT`, `CHECK (amount > 0)`. Index `ix_custpay_sale (tenant_id, sale_id)`.
  - Archetype A RLS: `tenant_isolation_policy` on `tenant_id`.
- Migration:
  - `20261005200108_020_CustomerPayments` applied via:
    `dotnet ef migrations add 020_CustomerPayments --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
    `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-509
### Credit Limit Check & Tenant-Global Consolidated Balance
- Schema & Entity Changes:
  - Column: `customers.credit_limit numeric(18,2) NULL`.
  - Domain Methods on `StoreOS.Domain.Customers.Customer`:
    - `public decimal? CreditLimit { get; private set; }`
    - `public void SetCreditLimit(decimal? limit)` (throws `invalid_credit_limit` if `limit < 0`).
- Credit Limit Checker (`StoreOS.Application.Sales.Checkout.CreditLimitChecker`):
  - Registered scoped: `AddScoped<CreditLimitChecker>()`.
  - Signature:
    ```csharp
    public class CreditLimitChecker
    {
        public CreditLimitChecker(
            IAppDbContext db,
            ICustomerLedgerService ledgerService,
            IAccessScope scope,
            IAuditWriter auditWriter);

        public async Task CheckAsync(Guid customerId, decimal newCreditAmount, CancellationToken ct);
    }
    ```
  - **Logic & Invariants:**
    - Early return when `newCreditAmount <= 0` without DB lookup.
    - Early return when `customer.CreditLimit is null` (unlimited credit).
    - Calls `_ledgerService.GetConsolidatedBalanceAsync(customerId, ct)` (reusing P-507's tenant-global cross-branch summation).
    - If `projectedBalance > customer.CreditLimit.Value`:
      - If caller has permission `sales:credit:override`: logs audit event (`sales.credit_limit_overridden` with projected balance, limit, customer info) and allows execution to proceed.
      - Otherwise throws `DomainValidationException("credit_limit_exceeded", ...)`.
- Migration:
  - `20261005200502_021_CreditLimit` applied via:
    `dotnet ef migrations add 021_CreditLimit --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
    `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`

## P-510
### CheckoutHandler Full Transaction Assembly
- Application Handler (`StoreOS.Application.Sales.Checkout.CheckoutHandler`):
  - Implements `IRequestHandler<CheckoutCommand, CheckoutResult>`.
  - Stateless, transaction-managed via MediatR open pipeline behavior `TransactionBehavior` (`ISerializableRequest`).
  - Single `SaveChangesAsync(ct)` at the very end ensuring complete atomicity across stock decrements, IMEI status changes, invoice creation, payments, and customer ledger postings.
  - **Corrected Ordering (Fixes Guid.Empty Defect):** `SaleItem` entities are constructed client-side FIRST, followed by stock decrements and `SellImeiAsync(line.ImeiRowId.Value, sale.Id, item.Id, ct)`, correctly threading each real `SaleItem.Id` into the IMEI row instead of `Guid.Empty`.
  - Full `Handle` method implementation:
    ```csharp
    public async Task<CheckoutResult> Handle(CheckoutCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId
            ?? throw new DomainValidationException("branch_context_required", "Branch context is required for checkout.");
        var saleDate = DateTimeOffset.UtcNow;
        var businessDate = DateOnly.FromDateTime(saleDate.UtcDateTime);

        var resolvedLines = new List<ResolvedCartLine>();
        foreach (var line in request.Lines)
            resolvedLines.Add(await _pricingResolver.ResolveAsync(branchId, line, ct));

        var subtotal = resolvedLines.Sum(l => l.PricePerUnit * l.Qty);
        var taxableBeforeDiscount = resolvedLines.Sum(l => l.LineTaxable);
        var cgstBeforeDiscount = resolvedLines.Sum(l => l.LineCgst);
        var sgstBeforeDiscount = resolvedLines.Sum(l => l.LineSgst);

        decimal totalTaxable = taxableBeforeDiscount;
        decimal totalCgst = cgstBeforeDiscount;
        decimal totalSgst = sgstBeforeDiscount;
        if (request.InvoiceDiscount > 0)
        {
            var beforeAgg = new LineTax(taxableBeforeDiscount, cgstBeforeDiscount, sgstBeforeDiscount);
            var afterAgg = DiscountEngine.ApplyProportionalDiscount(beforeAgg, subtotal, request.InvoiceDiscount);
            totalTaxable = afterAgg.Taxable;
            totalCgst = afterAgg.Cgst;
            totalSgst = afterAgg.Sgst;
        }
        var grandTotal = StoreOS.Domain.Common.Money.Round2(subtotal - request.InvoiceDiscount);

        Guid? customerId = request.CustomerId;
        var requestedPaid = request.Payments.Sum(p => p.Amount);
        var newCreditAmount = Math.Max(0, grandTotal - requestedPaid);
        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
            await _creditLimitChecker.CheckAsync(customerId.Value, newCreditAmount, ct);

        var seriesKey = $"FY{InvoiceNumberFormatter.FinancialYearLabel(businessDate)}";
        var branch = await _db.Set<StoreOS.Domain.Tenancy.Branch>().FirstAsync(b => b.Id == branchId, ct);
        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId, seriesKey, ct);
        var fyLabel = InvoiceNumberFormatter.FinancialYearLabel(businessDate);
        var invoiceNumber = request.InvoiceType == InvoiceType.TaxInvoice
            ? InvoiceNumberFormatter.FormatFyConsecutive(branch.Code, fyLabel, seq)
            : InvoiceNumberFormatter.FormatEstimate(branch.Code, fyLabel, seq);

        var customerName = request.WalkInCustomerName ?? "Walk-in Customer";
        string? customerGstin = null;
        if (customerId is not null)
        {
            var customerEntity = await _db.Set<Customer>().FirstOrDefaultAsync(c => c.Id == customerId.Value, ct);
            if (customerEntity != null)
            {
                customerName = customerEntity.Name;
                customerGstin = customerEntity.Gstin;
            }
        }

        var sale = request.InvoiceType == InvoiceType.TaxInvoice
            ? Sale.CreateTaxInvoice(
                _scope.TenantId, branchId, invoiceNumber, saleDate, customerId, customerName, customerGstin,
                totalTaxable, totalCgst, totalSgst, grandTotal, request.InvoiceDiscount, 0m,
                null, null, null, null, null, null, request.Description, null, _scope.UserId)
            : Sale.CreateEstimate(
                _scope.TenantId, branchId, invoiceNumber, saleDate, customerId, customerName, customerGstin,
                totalTaxable, totalCgst, totalSgst, grandTotal, request.InvoiceDiscount, 0m,
                null, null, null, null, null, null, request.Description, null, _scope.UserId);

        await _db.Set<Sale>().AddAsync(sale, ct);

        var itemsWithLines = new List<(SaleItem Item, ResolvedCartLine Line)>();
        foreach (var line in resolvedLines)
        {
            var item = SaleItem.Create(
                _scope.TenantId, sale.Id, line.ProductId, line.ProductName,
                line.Category, line.Qty, line.PricePerUnit, line.PurchasePriceSnapshot, line.HsnCode,
                line.IsMarginScheme, line.IsGift, line.LineTaxable, line.LineCgst, line.LineSgst, line.Imei);

            sale.AddItem(item);
            await _db.Set<SaleItem>().AddAsync(item, ct);
            itemsWithLines.Add((item, line));
        }

        if (request.InvoiceType == InvoiceType.TaxInvoice)
        {
            foreach (var (item, line) in itemsWithLines)
            {
                if (!StoreOS.Domain.Catalog.ProductCategory.IsPhysical(line.Category)) continue;

                await _stockService.ReserveAndDecrementStockAsync(
                    branchId, line.ProductId, line.Qty,
                    StoreOS.Domain.Inventory.MovementType.Sale, "Sale", sale.Id.ToString(), ct);

                if (line.ImeiRowId is not null)
                    await _stockService.SellImeiAsync(line.ImeiRowId.Value, sale.Id, item.Id, ct);
            }
        }

        var (payments, totalPaid) = PaymentComposer.Compose(
            _scope.TenantId, sale.Id, request.Payments, businessDate, _scope.UserId);

        foreach (var payment in payments)
            await _db.Set<CustomerPayment>().AddAsync(payment, ct);

        if (totalPaid > 0)
            sale.ApplyPayment(totalPaid);

        if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
        {
            await _ledgerService.RecordInvoiceDebitAsync(customerId.Value, sale.Id, grandTotal, branchId, businessDate, ct);
            if (totalPaid > 0)
                await _ledgerService.RecordPaymentCreditAsync(customerId.Value, sale.Id, totalPaid, branchId, businessDate, ct);

            var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == customerId.Value, ct);
            customer.AddToTotalPurchases(grandTotal);
        }

        await _audit.WriteAsync(
            branchId,
            "sale.checkout",
            "Sale",
            sale.Id.ToString(),
            new { invoiceNumber, grandTotal, totalPaid, request.InvoiceType },
            ct);

        await _db.SaveChangesAsync(ct);

        return new CheckoutResult(
            sale.Id,
            invoiceNumber,
            grandTotal,
            totalPaid,
            Math.Max(0, grandTotal - totalPaid),
            saleDate);
    }
    ```

## P-511
### SerializationRetryBehavior & Transaction Outer Retry Wrapper
- Pipeline Behavior (`StoreOS.Infrastructure.Persistence.SerializationRetryBehavior<TRequest, TResponse>`):
  ```csharp
  namespace StoreOS.Infrastructure.Persistence;

  public sealed class SerializationRetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
      where TRequest : notnull
  {
      private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
          .AddRetry(new RetryStrategyOptions
          {
              ShouldHandle = new PredicateBuilder().Handle<Exception>(IsSerializationFailure),
              MaxRetryAttempts = 3,
              BackoffType = DelayBackoffType.Exponential,
              UseJitter = true,
              Delay = TimeSpan.FromMilliseconds(25),
              MaxDelay = TimeSpan.FromMilliseconds(250)
          })
          .Build();

      public SerializationRetryBehavior(IServiceProvider services);
      public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct);
      public static bool IsSerializationFailure(Exception ex);
  }
  ```
- Pipeline Order (Registered in `Program.cs`):
  `ValidationBehavior -> SerializationRetryBehavior -> AuthorizationBehavior -> TransactionBehavior -> TokenVersionBehavior -> LiveDbPermissionBehavior -> IdempotencyBehavior -> Handler`
- Extended `CheckoutCommand` (carrying `IIdempotentRequest` and `ISerializableRequest`):
  ```csharp
  public record CheckoutCommand(...) : IRequest<CheckoutResult>, IRequiresPermission, ISerializableRequest, IIdempotentRequest
  {
      public string RequiredPermission => "pos:checkout";
      public string? IdempotencyKey { get; init; }
      public string IdempotencyScope => "pos.checkout";
      public bool IdempotencyRequired => true;
  }
  ```
- Tests & Verification:
  - Unit: `SerializationRetryBehaviorTests` (3 invocations on transient 40001 then success; exhaustion throws `DomainException("serialization_conflict")`; non-serialization exceptions not retried; read-only requests skipped).
  - Integration: `CheckoutContentionTests` & `IdempotencyBehaviorTests` (last unit parallel checkout with one 201 and one 422; 20 parallel checkouts all 201 with unique consecutive invoice numbers; same idempotency key replay; conflicting payload key reuse 409).

## P-512
### End-to-End Checkout Concurrency Test Suite
- Concurrency Test Suite (`StoreOS.ConcurrencyTests.EndToEndCheckoutConcurrencyTests`):
  - Driven through `IMediator.Send` in independently scoped `IServiceScope` instances per concurrent task, running against the `storeos_app` connection under full RLS session scopes.
  - **Scenario 1 — Last Unit Race:**
    - 5 concurrent checkouts competing for exactly 1 stock unit of a non-IMEI accessory.
    - Empirically verified across 5 consecutive iterations: exactly 1 checkout succeeds, the remaining 4 fail with `DomainValidationException("insufficient_stock")`, and final branch inventory stock qty is 0.
  - **Scenario 2 — IMEI Double-Sell:**
    - 3 concurrent checkouts competing for exactly 1 InStock IMEI of a new phone.
    - Empirically verified across 5 consecutive iterations: exactly 1 checkout succeeds, winning sale item ID is stamped onto the IMEI row, and losing tasks fail cleanly with `DomainValidationException` (observed failure codes: `insufficient_stock` / `imei_not_available`).
  - **Scenario 3 — Parallel Invoice Numbering Across Concurrent Checkouts:**
    - 20 concurrent checkouts competing on the `invoice_sequences` row for different products.
    - Empirically verified across 5 consecutive iterations: all 20 checkouts succeed, producing exactly 20 unique invoice numbers with no duplicate sequence numbers in PostgreSQL.

## P-513
### POST /api/pos/checkout Controller Endpoint
- Controller Action (`StoreOS.Api.Controllers.PosController`):
  ```csharp
  [HttpPost("checkout")]
  public async Task<IActionResult> Checkout(
      [FromBody] CheckoutCommand command,
      [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
      [FromServices] IdempotencyReplayFlag replayFlag,
      CancellationToken ct)
  ```
  - Enforces `Idempotency-Key` header presence upfront before dispatching to MediatR (returns 400 `idempotency_key_required` if missing/blank).
  - Dispatches `command with { IdempotencyKey = idempotencyKey }` to MediatR pipeline.
  - Detects cached replay via scoped `IdempotencyReplayFlag.Replayed` and appends `Idempotent-Replayed: true` response header.
  - Returns 201 Created with `Location: /api/sales/{saleId}` without requiring forward-reference stubs.
- Response JSON Shape:
  ```json
  {
    "saleId": "guid",
    "invoiceNumber": "string",
    "grandTotal": 0.00,
    "amountPaid": 0.00,
    "balance": 0.00,
    "saleDate": "2026-10-06T12:00:00Z"
  }
  ```
- Integration Tests:
  - `CheckoutControllerTests`: missing `Idempotency-Key` (400), valid checkout (201 with Location header), exact replay (201 + `Idempotent-Replayed: true` + 1 DB sale row), payload mismatch conflict (409 `idempotency_key_reuse`).

## P-514
### Gift Line Handling & Proportional Discount Wiring
- Handler Adjustments (`StoreOS.Application.Sales.Checkout.CheckoutHandler`):
  - **Chargeable Lines Aggregation:**
    ```csharp
    var chargeableLines = resolvedLines.Where(l => !l.IsGift).ToList();
    var subtotal = chargeableLines.Sum(l => l.PricePerUnit * l.Qty);
    var taxableBeforeDiscount = chargeableLines.Sum(l => l.LineTaxable);
    var cgstBeforeDiscount = chargeableLines.Sum(l => l.LineCgst);
    var sgstBeforeDiscount = chargeableLines.Sum(l => l.LineSgst);
    ```
  - Gift lines (`IsGift == true`) record nominal `price_per_unit` on `sale_items` for informational display and deduct inventory stock normally, but contribute `0` to financial totals (`subtotal`, `grandTotal`, taxes).
  - Proportional discount is applied strictly against the non-gift (chargeable) subtotal.
  - Pure giveaways (`grandTotal == 0`) with non-zero requested payments are explicitly rejected via `DomainValidationException("payment_exceeds_balance")`.
- Integration Tests:
  - `GiftAndDiscountCheckoutTests`: Scenario A (paid + gift line with correct grand total and stock deduction for both), Scenario B (proportional discount applied only across paid lines), Scenario C (pure giveaway zero grand total with non-zero payment rejection and zero payment success).

## P-515
### Finance/EMI Fields & Composition in Checkout
- Application Helper (`StoreOS.Application.Sales.Checkout.FinanceComposer`):
  ```csharp
  public record ComposedFinance(string FinanceProvider, string? FinancerName, decimal DownPayment,
      int TenureMonths, decimal AnnualInterestRate, decimal Principal, decimal MonthlyEmi, decimal TotalPayable);

  public static class FinanceComposer
  {
      public static ComposedFinance Compose(CheckoutFinanceDetails details, decimal grandTotal);
  }
  ```
- Domain Method on `Sale` (`StoreOS.Domain.Sales.Sale`):
  ```csharp
  public void RecordFinanceDetails(string financeProvider, string? financerName, decimal downPayment, string emiDetailsJson)
  {
      PaymentMode = Sales.PaymentMode.Finance;
      FinanceProvider = financeProvider;
      FinancerName = financerName;
      DownPayment = downPayment;
      EmiDetailsJson = emiDetailsJson;
  }
  ```
- Hard Invariant on Customer Ledger:
  - Financed sales are strictly excluded from `customer_ledger_entries` (neither debit nor credit is recorded, and credit limit check is bypassed).
  - The down payment is recorded as a real `CustomerPayment` and applied to `sale.AmountPaid`.
- Integration Tests:
  - `FinanceCheckoutTests`: Scenario A (0% interest EMI calculation and `emi_details` JSON persistence), Scenario B (positive interest rate EMI calculation matching FinanceEngine), Scenario C (customer ledger exclusion for financed sales while preserving down payment), Scenario D (`DownPayment > GrandTotal` throws `finance_down_payment_exceeds_total`).

## P-516
### Finance Providers Settings-Backed Suggestion List
- Application DTO and Use-Cases (`StoreOS.Application.Settings.FinanceProviders`):
  - `FinanceProviderDto(string NormalizedName, string DisplayName)`
  - `GetFinanceProvidersQuery : IRequest<IReadOnlyList<FinanceProviderDto>>, IReadOnlyRequest`
  - `AddFinanceProviderCommand(string DisplayName) : IRequest<Unit>, IRequiresPermission` (`settings:manage`)
  - `RemoveFinanceProviderCommand(string NormalizedName) : IRequest<Unit>, IRequiresPermission` (`settings:manage`)
- Controller Routes (`StoreOS.Api.Controllers.SettingsController`):
  - `GET /api/settings/finance-providers` -> `200 OK (IReadOnlyList<FinanceProviderDto>)`
  - `POST /api/settings/finance-providers` -> `200 OK` (Body: `{ displayName: string }`)
  - `DELETE /api/settings/finance-providers/{normalizedName}` -> `204 NoContent`
- Hard Invariant & Rules:
  - Suggestion list is backed by the tenant-level setting row `"finance_providers"` (`branch_id IS NULL`).
  - Deduplication is case-insensitive on normalized key (`displayName.Trim().ToLowerInvariant()`), while preserving the original casing of the first-added display name. Duplicate additions are idempotent no-ops.
  - Removing a provider removes the item from the suggestion list, but has strictly ZERO effect on existing `sales.finance_provider` / `sales.financer_name` snapshot values on historical sales.
- Integration Tests:
  - `FinanceProvidersTests`: adding provider, case-insensitive duplicate idempotency, provider removal, and empirical verification that historical sales retain their original provider string unaffected.

## P-517
### Estimate Support in Checkout (Bypass Stock/IMEI Decrement & Customer Ledger)
- Invariant 9 Proof & Guard Verification in `CheckoutHandler`:
  ```csharp
  // 1. Credit Limit Check bypass:
  if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice && request.Finance is null)
  {
      await _creditLimitChecker.CheckAsync(customerId.Value, newCreditAmount, ct);
  }

  // 2. Stock & IMEI reservation bypass:
  if (request.InvoiceType == InvoiceType.TaxInvoice)
  {
      foreach (var (item, line) in itemsWithLines)
      {
          if (!StoreOS.Domain.Catalog.ProductCategory.IsPhysical(line.Category)) continue;
          await _stockService.ReserveAndDecrementStockAsync(branchId, line.ProductId, line.Qty,
              StoreOS.Domain.Inventory.MovementType.Sale, "Sale", sale.Id.ToString(), ct);
          if (line.ImeiRowId is not null)
              await _stockService.SellImeiAsync(line.ImeiRowId.Value, sale.Id, item.Id, ct);
      }
  }

  // 3. Customer Ledger debit & total purchases bypass:
  if (customerId is not null && request.InvoiceType == InvoiceType.TaxInvoice)
  {
      if (request.Finance is null)
      {
          await _ledgerService.RecordInvoiceDebitAsync(customerId.Value, sale.Id, grandTotal, branchId, businessDate, ct);
          if (totalPaid > 0)
              await _ledgerService.RecordPaymentCreditAsync(customerId.Value, sale.Id, totalPaid, branchId, businessDate, ct);
      }
      var customer = await _db.Set<Customer>().FirstAsync(c => c.Id == customerId.Value, ct);
      customer.AddToTotalPurchases(grandTotal);
  }
  ```
- Numbering Series (DECISIONS D-07):
  - Estimates use a separate sequence series (`series_key = $"EST-FY{fyLabel}"`), formatted via `InvoiceNumberFormatter.FormatEstimate(...)` (e.g. `ET1/E/26-27/000001`).
  - Generating estimates does not consume or leave gaps in the official Rule 46(b) tax invoice consecutive series.
- Integration Tests (`EstimateCheckoutTests`):
  - Physical stock and inventory movements are completely untouched.
  - Serialized IMEIs remain `InStock` and unassigned.
  - Customer ledger entries are zero, and customer `total_purchases` remains untouched.
  - Credit limit checks are completely bypassed for estimates.
  - Distinct series numbering and status/type query filtering verified.

## P-518
### POST /api/pos/estimate Endpoint
- Controller Action & Request Shape (`StoreOS.Api.Controllers.PosController`):
  ```csharp
  public record CreateEstimateBody(
      IReadOnlyList<CheckoutCartLine> Lines,
      Guid? CustomerId,
      string? WalkInCustomerName,
      decimal InvoiceDiscount,
      IReadOnlyList<CheckoutPayment> Payments,
      string? Description);

  [HttpPost("estimate")]
  public async Task<IActionResult> CreateEstimate(
      [FromBody] CreateEstimateBody body,
      [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
      [FromServices] IdempotencyReplayFlag replayFlag,
      CancellationToken ct)
  ```
- Semantic & Architecture Invariants:
  - `CreateEstimateBody` deliberately omits `InvoiceType` and `Finance` fields, making it structurally impossible to accidentally create a financed estimate or specify the wrong invoice type.
  - Dispatches `CheckoutCommand` with forced `InvoiceType = InvoiceType.Estimate`, `Finance = null`, and `IdempotencyRequired = false`.
  - `Idempotency-Key` is completely optional: requests without the header succeed with 201 Created; requests with the header participate in `IdempotencyBehavior` (supporting replayed 201 responses and detecting payload mismatch 409 conflicts).
- Integration Tests (`CreateEstimateEndpointTests`):
  - Succeeded without `Idempotency-Key` (201 Created), stock unaffected, DB row confirmed as `InvoiceType.Estimate`.
  - Tested with `Idempotency-Key`: supports cached replay (`Idempotent-Replayed: true`) and detects payload mismatch conflict (409 `idempotency_key_reuse`).

## P-519
### converted_from_estimate_id / linked_tax_invoice_id Columns & Domain Methods
- Domain Entity Extensions (`StoreOS.Domain.Sales.Sale`):
  ```csharp
  public Guid? ConvertedFromEstimateId { get; private set; }
  public Guid? LinkedTaxInvoiceId { get; private set; }

  public void MarkConvertedFrom(Guid estimateId) => ConvertedFromEstimateId = estimateId;

  public void MarkConvertedToTaxInvoice(Guid taxInvoiceId)
  {
      if (InvoiceType != StoreOS.Domain.Sales.InvoiceType.Estimate)
          throw new StoreOS.Domain.Exceptions.DomainValidationException("not_an_estimate",
              "Only an Estimate can be marked as converted.");
      if (LinkedTaxInvoiceId is not null)
          throw new StoreOS.Domain.Exceptions.DomainValidationException("estimate_already_converted",
              "This estimate has already been converted to a tax invoice.");
      LinkedTaxInvoiceId = taxInvoiceId;
  }
  ```
- Database Columns & Constraints:
  - Added nullable self-referencing foreign key columns to `public.sales`:
    - `converted_from_estimate_id uuid NULL REFERENCES sales(id)`
    - `linked_tax_invoice_id uuid NULL REFERENCES sales(id)`
  - Mapped in `SaleConfiguration.cs` with foreign key relationships on delete `Restrict`.
- Migration:
  - `20261006192156_022_EstimateConversion` added and applied via:
    `dotnet ef migrations add 022_EstimateConversion --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
    `dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`
- Domain Tests (`StoreOS.Domain.Tests.Sales.EstimateConversionMarkersTests`):
  - `MarkConvertedFrom` correctly sets `ConvertedFromEstimateId` on invoice.
  - `MarkConvertedToTaxInvoice` correctly sets `LinkedTaxInvoiceId` on estimate.
  - Calling `MarkConvertedToTaxInvoice` on non-Estimate sale throws `DomainValidationException("not_an_estimate")`.
  - Calling `MarkConvertedToTaxInvoice` twice on same estimate throws `DomainValidationException("estimate_already_converted")`.

## P-520
### POST /api/pos/estimate/{id}/convert Handler & Endpoint
- Command & Response Shapes (`StoreOS.Application.Sales.ConvertEstimate`):
  ```csharp
  public record ConvertEstimateCommand(
      Guid EstimateId,
      IReadOnlyList<CheckoutPayment> Payments
  ) : IRequest<ConvertEstimateResult>, IRequiresPermission, ISerializableRequest, IIdempotentRequest
  {
      public string RequiredPermission => "pos:checkout";
      public string? IdempotencyKey { get; init; }
      public string IdempotencyScope => $"pos.estimate.convert:{EstimateId}";
      public bool IdempotencyRequired => true;
  }

  public record ConvertEstimateResult(
      Guid NewSaleId,
      string InvoiceNumber,
      decimal GrandTotal,
      decimal AmountPaid,
      decimal Balance);
  ```
- Handler Logic & Reuse (`StoreOS.Application.Sales.ConvertEstimate.ConvertEstimateHandler`):
  - Reuses core services: `IInventoryStockService`, `ICustomerLedgerService`, `CreditLimitChecker`, `IInvoiceSequenceAllocator`, `PaymentComposer`.
  - Implements Plan §10.7 11 steps:
    1. Verifies branch access and estimate status (`not_an_estimate`, `estimate_already_converted`, `branch_mismatch`).
    2. Re-validates live stock and IMEI availability.
    3. Runs customer credit limit check for unpaid balance.
    4. Reserves consecutive TaxInvoice number from tax series (`FYxx-xx`).
    5. Instantiates and saves new `TaxInvoice` `Sale` + `SaleItem` items.
    6. Decrements stock movements and marks IMEI sold with real `SaleItem.Id`.
    7. Records payments and posts debit/credit to `customer_ledger_entries`.
    8. Updates original estimate with `estimate.MarkConvertedToTaxInvoice(newSale.Id)` and stamps `newSale.MarkConvertedFrom(estimate.Id)`.
    9. Writes audit entry `estimate.converted`.
- Controller Endpoint (`StoreOS.Api.Controllers.PosController`):
  - `POST /api/pos/estimate/{id:guid}/convert`
  - Mandatory `Idempotency-Key` header (returns 400 `idempotency_key_required` if omitted).
  - Returns 201 Created with `Location: /api/sales/{newSaleId}`.
- Integration Tests (`ConvertEstimateTests`):
  - Happy path conversion with stock/IMEI decrements and bidirectional FK linkage.
  - Converting same estimate twice rejects with `estimate_already_converted`.
  - Stock sold out before conversion rejects with `insufficient_stock`.
  - IMEI sold before conversion rejects cleanly (`imei_not_available`/`insufficient_stock`).
  - Idempotency replay with same key returns cached result without double converting.
  - Attempting to convert regular tax invoice rejects with `not_an_estimate`.

## P-521
### GET /api/sales + Filters, Sorting, and Pagination
- Query & DTO Shapes (`StoreOS.Application.Sales.Search`):
  ```csharp
  public record GetSalesQuery(
      string? Search = null,
      string? DateFilter = null,
      DateOnly? CustomFrom = null,
      DateOnly? CustomTo = null,
      string? Status = null,
      string? PaymentMode = null,
      string? InvoiceType = null,
      bool? PaidOnly = null,
      bool? DueOnly = null,
      bool? FinancedOnly = null,
      string? Category = null,
      decimal? MinAmount = null,
      decimal? MaxAmount = null,
      string SortBy = "SaleDate",
      string SortDirection = "desc",
      int Page = 1,
      int PageSize = 50
  ) : IRequest<PagedResult<SaleSummaryDto>>, IReportingRequest;

  public record SaleSummaryDto(
      Guid Id,
      string InvoiceNumber,
      DateTimeOffset SaleDate,
      string CustomerName,
      decimal GrandTotal,
      decimal AmountPaid,
      decimal Balance,
      string Status,
      string InvoiceType,
      string? PaymentMode,
      bool IsFinanced);
  ```
- Controller Endpoint (`StoreOS.Api.Controllers.SalesController`):
  - `GET /api/sales` mapping query-string parameters to `GetSalesQuery`
  - Returns `200 OK` with `PagedResult<SaleSummaryDto>`
- Implementation Details (`StoreOS.Application.Sales.Search.GetSalesHandler`):
  - Read-heavy query executing against `IReportingDbContext` (read-only snapshot routing).
  - Universal search supporting invoice number, customer name, customer GSTIN, finance provider, financer name, description, and item names / IMEIs via `sale_items`.
  - Server-side date boundaries computed using tenant timezone (`Asia/Kolkata` mandatory, unsupported timezones throw `DomainValidationException("unsupported_timezone")`).
  - Date filters: `Today`, `Yesterday`, `ThisWeek`, `ThisMonth`, `LastMonth`, `ThisYear`, `Custom`.
  - Paid/Due filters (`AmountPaid >= GrandTotal` / `AmountPaid < GrandTotal`), Financed (`PaymentMode == "Finance"`), Category via `sale_items`.
  - Sorting by `SaleDate`, `GrandTotal`, `InvoiceNumber`, `CustomerName`, `Balance` (`GrandTotal - AmountPaid`).
- Integration Tests (`StoreOS.IntegrationTests.Sales.GetSalesTests`):
  - 37 tests covering search (invoice, IMEI, finance provider), all 7 date filters, status, payment modes, invoice types, paid/due, financed, category, amount ranges, 5 sort dimensions (asc/desc), and pagination boundaries.

## P-522
### GET /api/sales/{id} Detail Endpoint & Anti-Enumeration Mapping
- Query & DTO Shapes (`StoreOS.Application.Sales.GetSaleById`):
  ```csharp
  public record GetSaleByIdQuery(Guid Id) : IRequest<SaleDetailDto>, IReadOnlyRequest;

  public record SaleLineDto(
      Guid Id,
      string ItemName,
      string Category,
      int Qty,
      decimal PricePerUnit,
      decimal Taxable,
      decimal Cgst,
      decimal Sgst,
      bool IsMarginScheme,
      bool IsGift,
      string? Imei,
      string? HsnSnapshot);

  public record PaymentRecordDto(
      Guid Id,
      decimal Amount,
      string PaymentMode,
      DateOnly PaymentDate);

  public record SaleDetailDto(
      Guid Id,
      string InvoiceNumber,
      string InvoiceType,
      DateTimeOffset SaleDate,
      Guid? CustomerId,
      string CustomerName,
      string? CustomerGstin,
      decimal TotalTaxable,
      decimal TotalCgst,
      decimal TotalSgst,
      decimal GrandTotal,
      decimal Discount,
      decimal AmountPaid,
      decimal Balance,
      string Status,
      string? PaymentMode,
      string? FinanceProvider,
      string? FinancerName,
      decimal? DownPayment,
      string? EmiDetailsJson,
      string? GiftDetailsJson,
      string? Description,
      Guid? RepairJobId,
      Guid? ConvertedFromEstimateId,
      Guid? LinkedTaxInvoiceId,
      IReadOnlyList<SaleLineDto> Items,
      IReadOnlyList<PaymentRecordDto> Payments);
  ```
- Controller Endpoint (`StoreOS.Api.Controllers.SalesController`):
  - `GET /api/sales/{id:guid}`
  - Returns `200 OK` with `SaleDetailDto`
- Error Catalog & Anti-Enumeration (`StoreOS.Api.Middleware.ErrorCatalog`):
  - Added explicit mapping: `["sale_not_found"] = HttpStatusCode.NotFound` (404)
  - Anti-enumeration guarantee: querying a sale outside caller's tenant/branch access returns 404 Not Found rather than 403 Forbidden.
- Integration Tests (`StoreOS.IntegrationTests.Sales.GetSaleByIdTests`):
  - (a) Fetching full checkout sale returns all fields populated, line items, and payment records.
  - (b) Fetching nonexistent id returns 404 `sale_not_found`.
  - (c) Cross-tenant and cross-branch inaccessible sales return 404 (anti-enumeration verified).
  - (d) Converted estimate verification: tax invoice has `ConvertedFromEstimateId` populated; original estimate has `LinkedTaxInvoiceId` populated.

## P-523
### POST /api/sales/{id}/payments Subsequent Payment Endpoint
- Command & Response Shapes (`StoreOS.Application.Sales.RecordPayment`):
  ```csharp
  public record RecordPaymentCommand(
      Guid SaleId,
      decimal Amount,
      string PaymentMode
  ) : IRequest<RecordPaymentResult>, IRequiresPermission, ISerializableRequest, IIdempotentRequest
  {
      public string RequiredPermission => "pos:checkout";
      public string? IdempotencyKey { get; init; }
      public string IdempotencyScope => $"sales.payment:{SaleId}";
      public bool IdempotencyRequired => true;
  }

  public record RecordPaymentResult(Guid PaymentId, decimal NewAmountPaid, decimal NewBalance);
  ```
- Validator (`StoreOS.Application.Sales.RecordPayment.RecordPaymentValidator`):
  - `SaleId` required, `Amount > 0`, `PaymentMode` must be within `StoreOS.Domain.Sales.PaymentMode.All`.
- Controller Endpoint (`StoreOS.Api.Controllers.SalesController`):
  - `POST /api/sales/{id:guid}/payments`
  - Mandatory `Idempotency-Key` header (financial mutation).
  - Returns `200 OK` with `RecordPaymentResult`. Replay sets `Idempotent-Replayed: true`.
- Invariants Enforced (`StoreOS.Application.Sales.RecordPayment.RecordPaymentHandler`):
  - Payments are strictly additive and immutable: creates a new `CustomerPayment` row, never mutates existing ones (Invariant 5).
  - Re-reads sale inside the transaction and validates `amount <= outstanding` (`payment_exceeds_balance`).
  - Validates active sale status (`sale_not_active`).
  - Posts customer credit to `customer_ledger_entries` for `TaxInvoice` with attached customer.
  - Skips ledger posting entirely if `sale.PaymentMode == "Finance"`.
  - Writes audit event `sale.payment_recorded`.
- Integration Tests (`StoreOS.IntegrationTests.Sales.RecordPaymentTests`):
  - (a) Two separate partial payments create 2 distinct immutable rows in `customer_payments`, cumulative totals updated.
  - (b) Payment exceeding balance returns 422 `payment_exceeds_balance`.
  - (c) Payment against voided sale returns 422 `sale_not_active`.
  - (d) Payment creates customer ledger credit entry for tax invoice with attached customer.
  - (e) Payment against financed sale skips customer ledger entry creation.
  - (f) Replaying same `Idempotency-Key` does not double-record payment.

## P-524
### POST /api/sales/{id}/void Endpoint (Live Permission Gated, Reversing Movements & Ledger)
- Command Shape (`StoreOS.Application.Sales.VoidSale`):
  ```csharp
  public record VoidSaleCommand(Guid SaleId, string Reason) : IRequest, IHighImpactOperation
  {
      public string RequiredPermission => "sales:void";
  }
  ```
- Validator (`StoreOS.Application.Sales.VoidSale.VoidSaleValidator`):
  - `SaleId` required, `Reason` required.
- Controller Endpoint (`StoreOS.Api.Controllers.SalesController`):
  - `POST /api/sales/{id:guid}/void`
  - Live-DB permission gated (`sales:void` verified with zero cache lag via `IHighImpactOperation`).
  - Returns `200 OK`.
- Handler Implementation (`StoreOS.Application.Sales.VoidSale.VoidSaleHandler`):
  - Preserves original `Sale` row: marks `sale.MarkVoided()` (only status changes to `Voided`, amounts and lines preserved).
  - Throws `DomainValidationException("sale_not_active")` if not currently Active.
  - Corrected stock-reversal loop: restores physical stock for all physical lines (including gift lines since gifts consumed stock at sale time).
  - Reverses IMEI status (`imeiRow.RestoreToStock()`) transitioning `Sold -> InStock` and unlinking sale references.
  - Reverses customer ledger debit via `_ledgerService.RecordVoidReversalAsync` (`CustomerLedgerEntryType.InvoiceVoid`) and decreases `customer.SubtractFromTotalPurchases(sale.GrandTotal)`.
  - Skips ledger reversal if `PaymentMode == "Finance"`.
  - Writes audit event `sale.voided`.
- Integration Tests (`StoreOS.IntegrationTests.Sales.VoidSaleTests`):
  - (a) Voiding an active sale sets `status='Voided'` while preserving all original line items and financial amounts.
  - (b) Stock is restored for non-IMEI physical lines via `MovementType.SaleVoid`.
  - (c) IMEI transitions `Sold -> InStock`.
  - (d) Gift lines correctly have their stock restored on void.
  - (e) Voiding an already-voided sale returns 422 `sale_not_active`.
  - (f) Customer ledger gets `InvoiceVoid` credit and `total_purchases` decreases.
  - (g) Live-DB permission revocation check: caller with revoked DB permission receives 403 Forbidden immediately.

## P-525
### POST /api/sales/{id}/refund Endpoint (Whole/Partial Refund, Stock Reversal, Ledger Credit)
- Command Shape (`StoreOS.Application.Sales.RefundSale`):
  ```csharp
  public record RefundSaleCommand(
      Guid SaleId,
      IReadOnlyList<Guid>? SaleItemIds,
      string Reason,
      decimal RefundAmount
  ) : IRequest, IHighImpactOperation
  {
      public string RequiredPermission => "sales:refund";
  }
  ```
- Validator (`StoreOS.Application.Sales.RefundSale.RefundSaleValidator`):
  - `SaleId` required, `Reason` required, `RefundAmount > 0`.
- Controller Endpoint (`StoreOS.Api.Controllers.SalesController`):
  - `POST /api/sales/{id:guid}/refund`
  - Live-DB permission gated (`sales:refund` verified via `IHighImpactOperation`).
  - Returns `200 OK`.
- Partial-vs-Whole Refund Invariants (`StoreOS.Application.Sales.RefundSale.RefundSaleHandler`):
  - Original `Sale` row financial amounts are preserved and immutable (Invariant 6).
  - Whole-sale refund (`SaleItemIds == null` or all lines selected): transitions `sale.Status` to `Refunded`.
  - Partial refund: leaves `sale.Status` as `Active`, as requirements define only 3 states (`Active`, `Voided`, `Refunded`) and the audit trail captures which lines were refunded.
  - Restores stock for refunded physical lines (`MovementType.SaleRefund`) and restores IMEI status to `InStock`.
  - Customer ledger gets a `Refund` credit for the exact `RefundAmount` (which can reflect restocking fee deductions), and `customer.SubtractFromTotalPurchases(request.RefundAmount)` decreases purchases by that exact amount.
  - Skips ledger credit if `PaymentMode == "Finance"`.
  - Writes audit event `sale.refunded`.
- Integration Tests (`StoreOS.IntegrationTests.Sales.RefundSaleTests`):
  - (a) Whole-sale refund sets `Status='Refunded'`, restores all physical stock and IMEIs, original totals preserved.
  - (b) Partial refund leaves `Status='Active'`, only specified line restored, audit trail records line ids and partial flag.
  - (c) Refund amount exceeding grand total returns 422 `invalid_refund_amount`.
  - (d) Refunding a voided sale returns 422 `sale_not_active`.
  - (e) Restocking fee partial amount applies exact refund amount to customer ledger and decreases `total_purchases` by exact amount.
  - (f) Live-DB permission revocation check: caller with revoked permission receives 403 Forbidden immediately.

## P-601
### GET /api/customers/{id}/credit-balance Endpoint
- Query & DTO (`StoreOS.Application.Customers.GetCreditBalance`):
  ```csharp
  public record GetCreditBalanceQuery(Guid CustomerId) : IRequest<CreditBalanceDto>, IReadOnlyRequest;

  public record CreditBalanceDto(
      Guid CustomerId,
      decimal ConsolidatedBalance,
      decimal? CreditLimit,
      decimal? AvailableCredit,
      bool IsOverLimit);
  ```
- Handler (`StoreOS.Application.Customers.GetCreditBalance.GetCreditBalanceHandler`):
  - Fetches customer by id and tenant id.
  - Calls `ICustomerLedgerService.GetConsolidatedBalanceAsync(customerId, ct)` for the single sanctioned cross-branch balance calculation.
  - Calculates `AvailableCredit = CreditLimit - ConsolidatedBalance` (null if CreditLimit is null).
  - Calculates `IsOverLimit = CreditLimit is not null && ConsolidatedBalance > CreditLimit`.
- Controller (`StoreOS.Api.Controllers.CustomersController`):
  - `GET /api/customers/{id:guid}/credit-balance` -> returns `CreditBalanceDto`.
- Integration Tests (`StoreOS.IntegrationTests.Customers.GetCreditBalanceTests`):
  - (a) Customer with no credit limit and outstanding balance -> `CreditLimit=null`, `AvailableCredit=null`, `IsOverLimit=false`.
  - (b) Customer with limit and balance under it -> correct `AvailableCredit`, `IsOverLimit=false`.
  - (c) Customer over limit -> correct negative `AvailableCredit`, `IsOverLimit=true`.
  - (d) Cross-branch customer -> balance correctly consolidated across multiple branches.

## P-602
### Customers CRUD Endpoints
- Use-Case Slices (`StoreOS.Application.Customers.Crud`):
  - `CreateCustomerCommand(string Name, string? Phone = null, string? Gstin = null) : IRequest<Guid>, IRequiresPermission`
    - Gated by `pos:checkout`.
  - `GetCustomersQuery(string? Search = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<CustomerListItemDto>>, IReadOnlyRequest`
    - `CustomerListItemDto(Guid Id, string Name, string? Phone, decimal TotalPurchases, decimal CurrentCreditBalance)`
    - `CurrentCreditBalance` computed via `ICustomerLedgerService.GetConsolidatedBalanceAsync` for each customer on page.
  - `GetCustomerByIdQuery(Guid Id) : IRequest<CustomerDetailDto>, IReadOnlyRequest`
    - `CustomerDetailDto(Guid Id, string Name, string? Phone, string? Gstin, decimal TotalPurchases, decimal? CreditLimit, decimal CurrentCreditBalance, DateTimeOffset CreatedAt)`
  - `UpdateCustomerProfileCommand(Guid Id, string Name, string? Phone = null, string? Gstin = null) : IRequest, IRequiresPermission`
    - Gated by `pos:checkout` for general front-line profile edits.
  - `SetCustomerCreditLimitCommand(Guid Id, decimal? CreditLimit) : IRequest, IRequiresPermission`
    - Split deliberately from profile edits: gated by `settings:manage`.
- Controller (`StoreOS.Api.Controllers.CustomersController`):
  - `GET /api/customers` -> `PagedResult<CustomerListItemDto>`
  - `POST /api/customers` -> returns 201 Created with Location header and customer id
  - `GET /api/customers/{id:guid}` -> `CustomerDetailDto`
  - `PATCH /api/customers/{id:guid}` -> 204 NoContent
  - `PATCH /api/customers/{id:guid}/credit-limit` -> 204 NoContent
- Integration Tests (`StoreOS.IntegrationTests.Customers.CustomersCrudTests`):
  - (a) Create, get by id, and update profile all work correctly.
  - (b) `PATCH .../credit-limit` requires `settings:manage` specifically (returns 403 for user with only `pos:checkout`, succeeds with 204 for manager).
  - (c) Search by partial phone number and partial name both work correctly.
  - (d) `CurrentCreditBalance` in `CustomerListItemDto` accurately matches ledger balance.

## P-603
### GET /api/customers/{id}/ledger and /api/customers/{id}/sales Endpoints
- Query & DTO shapes (`StoreOS.Application.Customers.GetCustomerLedger`, `StoreOS.Application.Customers.GetCustomerSales`):
  ```csharp
  public record GetCustomerLedgerQuery(Guid CustomerId, int Page = 1, int PageSize = 50)
      : IRequest<PagedResult<CustomerLedgerEntryDto>>, IReadOnlyRequest;

  public record CustomerLedgerEntryDto(
      long Id,
      string EntryType,
      decimal Debit,
      decimal Credit,
      Guid? BranchId,
      string? BranchName,
      DateOnly EntryDate,
      string? RefId);

  public record GetCustomerSalesQuery(Guid CustomerId, int Page = 1, int PageSize = 50)
      : IRequest<PagedResult<SaleSummaryDto>>, IReadOnlyRequest;
  ```
- Handlers:
  - `GetCustomerLedgerHandler`: DETAIL view, branch-filtered (applies standard branch-scoped query filters per Decision 4 / P-506 two-query pattern). Joins `branches` table for `BranchName`.
  - `GetCustomerSalesHandler`: Paginated sales history for a given customer (`sales.customer_id == request.CustomerId`), reusing `SaleSummaryDto` from P-521.
- Controller (`StoreOS.Api.Controllers.CustomersController`):
  - `GET /api/customers/{id:guid}/ledger` -> `PagedResult<CustomerLedgerEntryDto>`
  - `GET /api/customers/{id:guid}/sales` -> `PagedResult<SaleSummaryDto>`
- Explicit Scoping Contrast:
  - `GET /api/customers/{id}/credit-balance` (P-601): tenant-global checkout check (`IgnoreQueryFilters()`), aggregating balances across all branches.
  - `GET /api/customers/{id}/ledger` (P-603): branch-filtered detail view, only returning ledger rows accessible to the caller's active branch scope.
- Integration Tests (`StoreOS.IntegrationTests.Customers.CustomerLedgerAndSalesViewTests`):
  - (a) Ledger view displays branch attribution per entry with `BranchId` and `BranchName`.
  - (b) A branch-A-scoped user viewing a multi-branch customer's ledger sees only branch-A entries, while the balance endpoint returns the consolidated total across branches.
  - (c) Sales view lists only the customer's sales, branch-scoped and paginated.

## P-604
### POST /api/customers/{id}/anonymize (DPDP Act PII Erasure)
- Domain Methods:
  - `Customer.Anonymize()`: Scrubs customer's name to `"Anonymized Customer"`, nulls out `phone` and `gstin`.
  - `Sale.ScrubCustomerNameSnapshot(string replacement)`:
    - **CRITICAL / SOLE EXCEPTION TO INVARIANT 4**: This method is the single, explicit, legally-mandated DPDP Act exception to historical invoice snapshot immutability across the entire codebase. It mutates *only* the display-name string (`sales.customer_name`), leaving all fiscal figures (`grand_total`, `total_taxable`, `total_cgst`, `total_sgst`, `amount_paid`, line tax amounts, etc.) completely untouched per Section 36 CGST 72-month retention requirement.
- Command & Handler (`StoreOS.Application.Customers.Anonymize`):
  - `public record AnonymizeCustomerCommand(Guid CustomerId) : IRequest, IHighImpactOperation`
    - Live-DB permission gated with `settings:manage`.
  - `AnonymizeCustomerHandler`:
    - Calls `customer.Anonymize()`.
    - Updates `sale.ScrubCustomerNameSnapshot("Anonymized Customer")` for all sales of this customer across the tenant.
    - Writes audit log event `customer.anonymized`.
- Controller (`StoreOS.Api.Controllers.CustomersController`):
  - `POST /api/customers/{id:guid}/anonymize` -> 204 NoContent.
- Integration Tests (`StoreOS.IntegrationTests.Customers.AnonymizeCustomerTests`):
  - (a) Live-DB check: caller without `settings:manage` gets 403 Forbidden.
  - (b) Customer PII scrubbed: name becomes "Anonymized Customer", phone and gstin null.
  - (c) Sales referencing customer have `customer_name` scrubbed, while all fiscal data (`grand_total`, taxes, invoice number, items) remains byte-for-byte identical.
  - (d) Customer ledger entries are preserved untouched (erasure targets PII, not financial audit trail).

## P-605
### Supplier Transactions & Supplier Ledger Entities and Migration
- Domain Types (`StoreOS.Domain.Suppliers`):
  - `SupplierTransactionType`: `Bill`, `Payment`, `OpeningBalance`.
  - `SupplierPaymentMode`: `Cash`, `UPI`, `BankTransfer`, `Cheque`, `Credit`.
  - `SupplierLedgerEntryType`: `Bill`, `Payment`, `OpeningBalance`, `ReturnCredit`.
  - `SupplierTransaction`:
    - Factory: `Create(Guid tenantId, Guid supplierId, Guid? branchId, string type, decimal amount, string? paymentMode, string? referenceNo, DateOnly txnDate, string? notes, Guid createdBy)`
    - Validates: `amount > 0`, `type` in valid set, `paymentMode` required & valid when `type == Payment`, valid if non-null for other types.
  - `SupplierLedgerEntry`:
    - Factories: `CreateDebit(Guid tenantId, Guid supplierId, Guid? branchId, string entryType, decimal amount, DateOnly entryDate, string? refId)`, `CreateCredit(...)`
    - Validates: `amount > 0`, `entryType` in valid set.
- Explicit Polarity Convention (Opposite of Customer Ledger):
  - For `supplier_ledger_entries`:
    - `Credit` increases payable owed to supplier (`Bill`, `OpeningBalance`).
    - `Debit` decreases payable owed to supplier (`Payment`, `ReturnCredit`).
    - Payable calculation: `SUM(credit) - SUM(debit)`.
- Persistence Configurations & Migration:
  - EF Configurations: `SupplierTransactionConfiguration`, `SupplierLedgerEntryConfiguration`.
  - CHECK constraints: `type IN ('Bill','Payment','OpeningBalance')`, `amount > 0`, `payment_mode IS NULL OR payment_mode IN ('Cash','UPI','BankTransfer','Cheque','Credit')`.
  - Migration: `20261006203222_023_SupplierTransactions` created via:
    `dotnet ef migrations add 023_SupplierTransactions --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
    applied via:
    `dotnet ef database update --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - Tables: `supplier_transactions`, `supplier_ledger_entries`. Both have tenant isolation RLS enabled and forced.
  - Nullable-branch query filters registered in `QueryFilters.ApplyAll`.
- Tests:
  - Domain tests: `StoreOS.Domain.Tests.Suppliers.SupplierTransactionAndLedgerTests` covering valid transaction types, payment mode validation, debit/credit factories.
  - RLS coverage tests: `StoreOS.IntegrationTests.RlsCoverageTests` all 5 passing.

## P-606
### Supplier Transactions Endpoints & Payable Derivation
- Application Ledger Interface (`StoreOS.Application.Suppliers.ISupplierLedgerService`):
  ```csharp
  public interface ISupplierLedgerService
  {
      Task RecordBillAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
      Task RecordPaymentAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
      Task RecordOpeningBalanceAsync(Guid supplierId, decimal amount, DateOnly entryDate, CancellationToken ct);
      Task RecordReturnCreditAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
      Task<decimal> GetPayableAsync(Guid supplierId, CancellationToken ct);
  }
  ```
- Infrastructure Implementation (`StoreOS.Infrastructure.Suppliers.SupplierLedgerService`):
  - Registered scoped in `Program.cs`.
  - `RecordBillAsync`: creates Credit entry with `SupplierLedgerEntryType.Bill`.
  - `RecordPaymentAsync`: creates Debit entry with `SupplierLedgerEntryType.Payment`.
  - `RecordOpeningBalanceAsync`: creates Credit entry with `SupplierLedgerEntryType.OpeningBalance` and null branch.
  - `RecordReturnCreditAsync`: creates Debit entry with `SupplierLedgerEntryType.ReturnCredit`.
  - `GetPayableAsync`: derives payable across tenant using `Math.Max(0, SUM(credit) - SUM(debit))` with `IgnoreQueryFilters()`.
- Vertical Slice Shapes (`StoreOS.Application.Suppliers`):
  - `RecordTransaction`:
    - `RecordSupplierTransactionCommand(Guid SupplierId, string Type, decimal Amount, string? PaymentMode = null, string? ReferenceNo = null, DateOnly TxnDate = default, string? Notes = null) : IRequest<Guid>, IRequiresPermission` (`purchases:manage`)
    - `RecordSupplierTransactionValidator`
    - `RecordSupplierTransactionHandler`
  - `GetSupplierPayable`:
    - `GetSupplierPayableQuery(Guid SupplierId) : IRequest<SupplierPayableDto>, IReadOnlyRequest, IRequiresPermission` (`purchases:manage`)
    - `SupplierPayableDto(Guid SupplierId, decimal Balance)`
    - `GetSupplierPayableHandler`
  - `GetSupplierTransactions`:
    - `GetSupplierTransactionsQuery(Guid SupplierId, DateOnly? FromDate = null, DateOnly? ToDate = null, string? Type = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<SupplierTransactionDto>>, IReadOnlyRequest, IRequiresPermission` (`purchases:manage`)
    - `SupplierTransactionDto(Guid Id, Guid SupplierId, Guid? BranchId, string Type, decimal Amount, string? PaymentMode, string? ReferenceNo, DateOnly TxnDate, string? Notes, Guid CreatedBy, DateTimeOffset CreatedAt)`
    - `GetSupplierTransactionsHandler`
- Controller Endpoints (`StoreOS.Api.Controllers.SuppliersController`):
  - `POST /api/suppliers/{id:guid}/transactions` -> `RecordTransaction` (returns `{ id = txnId }`)
  - `GET /api/suppliers/{id:guid}/ledger` (and alias `/api/suppliers/{id:guid}/payable`) -> `GetPayable` (returns `SupplierPayableDto`)
  - `GET /api/suppliers/{id:guid}/transactions` -> `GetTransactions` (returns `PagedResult<SupplierTransactionDto>`)
- Integration Tests (`StoreOS.IntegrationTests.Suppliers.SupplierTransactionsTests`):
  - (a) Recording an OpeningBalance of 10000 -> GetPayableAsync returns 10000.
  - (b) Recording a Bill of 5000 -> payable becomes 15000.
  - (c) Recording a Payment of 8000 -> payable becomes 7000.
  - (d) Formula exactly matches Requirements Section 13.4: `Payable = Opening + Bills - Payments - Credits`.
  - (e) Caller without `purchases:manage` -> 403 Forbidden on all 3 endpoints.
  - (f) Listing transactions with date-range filter works correctly.

## P-607
### Supplier Returns & Supplier Return Items Entities and Migration
- Domain Types (`StoreOS.Domain.Suppliers`):
  - `SettlementType`: `PayableDeduction`, `CashRefund`, `PendingCredit`.
  - `SupplierReturnNumberFormatter`:
    - Shape: `RET-<BRANCH>-YYYYMMDD-XXX`
    - Methods:
      - `Format(string branchCode, DateOnly date, long seq)`
      - `Format(string branchCode, DateTimeOffset date, long seq)`
  - `SupplierReturn`:
    - Aggregate root with private `List<SupplierReturnItem> _items`.
    - Factory: `Create(Guid tenantId, Guid branchId, string returnNumber, DateOnly returnDate, Guid supplierId, string? supplierPhone, string? supplierAddress, string settlementType, string? reason, string? notes, Guid createdBy)`
    - Methods: `AddItem(SupplierReturnItem item)`, `AddItem(Guid? productId, string itemName, int qty, decimal purchasePrice, decimal returnRate, string[]? imeiNumbers = null)`
    - Properties: `Id`, `TenantId`, `BranchId`, `ReturnNumber`, `ReturnDate`, `SupplierId`, `SupplierPhone`, `SupplierAddress`, `TotalQty`, `TotalAmount`, `SettlementType`, `Reason`, `Notes`, `CreatedBy`, `CreatedAt`, `Items`.
  - `SupplierReturnItem`:
    - Factory: `Create(Guid tenantId, Guid returnId, Guid? productId, string itemName, int qty, decimal purchasePrice, decimal returnRate, string[]? imeiNumbers = null)`
    - Properties: `Id`, `TenantId`, `ReturnId`, `ProductId`, `ItemName`, `Qty`, `PurchasePrice`, `ReturnRate`, `LineTotal`, `ImeiNumbers`.
    - Validation: `qty > 0`, `purchasePrice >= 0`, `returnRate >= 0`, `itemName` required.
- Persistence Configurations & Migration:
  - Configurations: `SupplierReturnConfiguration`, `SupplierReturnItemConfiguration`.
  - Constraints:
    - `CK_supplier_returns_settlement_type`: `settlement_type IN ('PayableDeduction', 'CashRefund', 'PendingCredit')`
    - `CK_supplier_return_items_qty`: `qty > 0`
    - `UQ_supplier_returns_tenant_return_number`: `UNIQUE (tenant_id, return_number)`
    - Array column: `imei_numbers text[]` with `string[]` CLR property.
  - Migration: `20261006205902_024_SupplierReturns` created via:
    `dotnet ef migrations add 024_SupplierReturns --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
    applied via:
    `dotnet ef database update --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - RLS policies:
    - `supplier_returns`: Archetype B (`tenant_branch_isolation_policy`).
    - `supplier_return_items`: Archetype A (`tenant_isolation_policy`).
    - Both tables have `ENABLE ROW LEVEL SECURITY` and `FORCE ROW LEVEL SECURITY`.
  - QueryFilters:
    - `SupplierReturn`: branch-scoped filter in `QueryFilters.ApplyAll`.
    - `SupplierReturnItem`: tenant-scoped filter in `QueryFilters.ApplyAll`.
- Tests:
  - Domain tests: `StoreOS.Domain.Tests.Suppliers.SupplierReturnTests` (8 tests passing), `StoreOS.Domain.Tests.Suppliers.SupplierReturnNumberFormatterTests` (9 tests passing).
  - RLS coverage tests: `StoreOS.IntegrationTests.Schema.RlsCoverageTests` (5 tests passing).

## P-608
### POST /api/supplier-returns Handler & Atomic Orchestration
- Command & Result Types (`StoreOS.Application.Suppliers.CreateReturn`):
  - `ReturnLineInput(Guid? ProductId, string ItemName, int Qty, decimal PurchasePrice, decimal ReturnRate, string[]? ImeiNumbers = null)`
  - `CreateSupplierReturnCommand(Guid SupplierId, DateOnly ReturnDate, IReadOnlyList<ReturnLineInput> Lines, string SettlementType, string? Reason = null, string? Notes = null) : IRequest<CreateSupplierReturnResult>, IRequiresPermission` (`purchases:manage`)
  - `CreateSupplierReturnResult(Guid ReturnId, string ReturnNumber, decimal TotalAmount)`
  - `CreateSupplierReturnValidator` (FluentValidation)
  - `CreateSupplierReturnHandler`
- Controller Route (`StoreOS.Api.Controllers.SupplierReturnsController`):
  - `POST /api/supplier-returns` -> `Create([FromBody] CreateSupplierReturnCommand command)` -> 200 OK (`CreateSupplierReturnResult`).
- Atomic Orchestration Flow:
  1. Validates branch context (`_branchContext.CurrentBranchId`).
  2. Allocates sequential daily return number via `_sequenceAllocator` using series key `$"RETURN-{request.ReturnDate:yyyyMMdd}"` and formats via `SupplierReturnNumberFormatter.Format(branch.Code, request.ReturnDate, seq)` (`RET-<BRANCH>-YYYYMMDD-XXX`).
  3. Creates `SupplierReturn` header and flushes via preliminary `SaveChangesAsync()` so `supplierReturn.Id` is persisted as a real UUID `refId`.
  4. For each line: decrements physical stock conditionally (`ReserveAndDecrementStockAsync` with `MovementType.SupplierReturn` and `refId = supplierReturn.Id.ToString()`), creates `SupplierReturnItem`, transitions any listed IMEIs to `ReturnedToSupplier` (`imeiRow.MarkReturnedToSupplier(supplierReturn.Id)`), and accumulates totals.
  5. Sets header totals via `supplierReturn.SetTotals(totalQty, totalAmount)`.
  6. Ledger posting based on settlement type (Plan §10.4):
     - `PayableDeduction`: calls `_supplierLedger.RecordReturnCreditAsync(...)` (reduces payable).
     - `PendingCredit`: calls `_supplierLedger.RecordReturnCreditAsync(...)` (records credit available for future bills).
     - `CashRefund`: deliberately DOES NOT post to ledger (immediate cash transaction outside payable tracking; documented design decision).
  7. Audit log written: `supplier_return.created` with return metadata.
  8. Final `SaveChangesAsync()` commits the entire atomic unit.
- Integration Tests (`StoreOS.IntegrationTests.Suppliers.CreateSupplierReturnTests`):
  - (a) Return with non-IMEI line decrements stock and records `SupplierReturn` movement.
  - (b) Return with IMEI line transitions IMEI to `ReturnedToSupplier` and verifies it becomes unavailable for sale (rejected by `LoadImeiForSaleAsync`).
  - (c) `PayableDeduction` settlement reduces supplier payable via `GetPayableAsync`.
  - (d) `CashRefund` settlement leaves payable unaffected.
  - (e) `PendingCredit` settlement posts return credit reducing payable.
  - (f) Returning more than available stock fails with `insufficient_stock` (422) and rolls back atomically.
  - (g) Unique, sequential return numbers formatted correctly per branch and date.

## P-609
### Expenses Entity, Migration & CRUD Endpoints
- Domain Types (`StoreOS.Domain.FinanceOps`):
  - `ExpenseCategory`:
    - Constants: `Rent`, `Utilities`, `Salaries`, `Miscellaneous`.
    - `Suggested`: `IReadOnlyList<string>` suggestions only (extensible free text; not an enum).
  - `Expense`:
    - Factory: `Create(Guid tenantId, Guid? branchId, string description, decimal amount, string category, DateOnly expenseDate, string? notes, Guid createdBy)`
    - Validates: `description` required, `amount > 0`, `category` required (any non-empty string allowed per Requirements §18.3), `expenseDate` required.
    - Properties: `Id`, `TenantId`, `BranchId` (nullable, NULL = head office), `Description`, `Amount`, `Category`, `ExpenseDate`, `Notes`, `CreatedBy`, `CreatedAt`.
- Persistence Configurations & Migration:
  - Configuration: `ExpenseConfiguration`.
  - Table: `expenses` (columns: `id`, `tenant_id`, `branch_id`, `description`, `amount`, `category`, `expense_date`, `notes`, `created_by`, `created_at`).
  - Constraints: `CK_expenses_amount` (`amount > 0`), foreign key to `branches(id)`.
  - Indexes: `ix_expenses_tenant_branch_date (tenant_id, branch_id, expense_date)`.
  - Migration: `20261006211923_025_Expenses` created via:
    `dotnet ef migrations add 025_Expenses --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
    applied via:
    `dotnet ef database update --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - RLS: `tenant_branch_isolation_policy` on `expenses` with sentinel UUID handling for nullable `branch_id` (`COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid) = '00000000-0000-0000-0000-000000000000'::uuid`).
  - QueryFilters: nullable-branch filter registered in `QueryFilters.ApplyAll`.
- Vertical Slices (`StoreOS.Application.FinanceOps.Expenses`):
  - `CreateExpense`:
    - `CreateExpenseCommand(string Description, decimal Amount, string Category, DateOnly ExpenseDate, Guid? BranchId = null, string? Notes = null) : IRequest<Guid>, IRequiresPermission` (`expenses:manage`)
    - `CreateExpenseValidator`
    - `CreateExpenseHandler`
  - `GetExpenses`:
    - `GetExpensesQuery(DateOnly? From = null, DateOnly? To = null, string? Category = null, Guid? BranchId = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<ExpenseDto>>, IReadOnlyRequest, IRequiresPermission` (`expenses:manage`)
    - `ExpenseDto(Guid Id, Guid? BranchId, string Description, decimal Amount, string Category, DateOnly ExpenseDate, string? Notes, Guid CreatedBy, DateTimeOffset CreatedAt)`
    - `GetExpensesHandler`: defaults date range to `[first day of current month, current date]` when both `From` and `To` are omitted per Requirements §18.5.
  - `GetMonthlyTotal`:
    - `GetMonthlyTotalQuery(int? Year = null, int? Month = null, Guid? BranchId = null) : IRequest<MonthlyExpenseTotalDto>, IReadOnlyRequest, IRequiresPermission` (`expenses:manage`)
    - `MonthlyExpenseTotalDto(int Year, int Month, decimal TotalAmount)`
    - `GetMonthlyTotalHandler`: sums all expenses for the specified or current month.
- Controller Endpoints (`StoreOS.Api.Controllers.ExpensesController`):
  - `POST /api/expenses` -> `Create([FromBody] CreateExpenseCommand command)` -> 200 OK (`{ id }`)
  - `GET /api/expenses?from=&to=&category=&branchId=&page=&pageSize=` -> `List(...)` -> 200 OK (`PagedResult<ExpenseDto>`)
  - `GET /api/expenses/monthly-total?year=&month=&branchId=` -> `GetMonthlyTotal(...)` -> 200 OK (`MonthlyExpenseTotalDto`)
- Integration Tests (`StoreOS.IntegrationTests.FinanceOps.ExpensesTests`):
  - (a) Creating with novel category string succeeds, proving category extensibility.
  - (b) Empty description, zero amount, missing date, and empty category throw specific validation codes (`expense_description_required`, `expense_invalid_amount`, `expense_date_required`, `expense_category_required`).
  - (c) `GET /api/expenses` with no date range defaults to first-of-month through today.
  - (d) Monthly total sums only the current month's expenses.
  - RLS coverage tests: `StoreOS.IntegrationTests.Schema.RlsCoverageTests` (5 tests passing).

## P-610
### Liabilities & Liability Payments Entities and Migration
- Domain Types (`StoreOS.Domain.FinanceOps`):
  - `LiabilityType`:
    - Constants: `Loan`, `CreditCard`, `Financing`, `Investment`.
    - Set: `All = new HashSet<string> { Loan, CreditCard, Financing, Investment }`.
    - Method: `IsValid(string type) => All.Contains(type)`.
  - `Liability`:
    - Strictly tenant-level (no `branch_id` column per Plan §8.8).
    - Factory: `Create(Guid tenantId, string title, string type, string? lenderName, decimal totalPrincipal, DateOnly? dueDate, string? notes)`.
    - Methods:
      - `UpdateDescriptiveData(string title, string? lenderName, DateOnly? dueDate, string? status, string? notes)`: updates title, lender, due date, status, notes only.
    - Immutable-Principal Rule: `TotalPrincipal` is strictly immutable after creation. `UpdateDescriptiveData` deliberately never accepts `totalPrincipal` (Requirements §19.6: editing must not rewrite repayment history).
    - Properties: `Id`, `TenantId`, `Title`, `Type`, `LenderName`, `TotalPrincipal`, `DueDate`, `Status`, `Notes`, `CreatedAt`.
  - `LiabilityPayment`:
    - Factory: `Create(Guid tenantId, Guid liabilityId, decimal amount, string paymentMode, DateOnly paymentDate, string? notes)`.
    - Immutable-Payment Rule: permanently immutable with no update/mutation methods, identical in spirit to `CustomerPayment` (P-508).
    - Properties: `Id`, `TenantId`, `LiabilityId`, `Amount`, `PaymentMode`, `PaymentDate`, `Notes`, `CreatedAt`.
- Persistence Configurations & Migration:
  - Configurations: `LiabilityConfiguration`, `LiabilityPaymentConfiguration`.
  - Tables:
    - `liabilities`: `id`, `tenant_id`, `title`, `type`, `lender_name`, `total_principal`, `due_date`, `status`, `notes`, `created_at`.
    - `liability_payments`: `id`, `tenant_id`, `liability_id`, `amount`, `payment_mode`, `payment_date`, `notes`, `created_at`.
  - Constraints:
    - `CK_liabilities_type`: `type IN ('Loan', 'CreditCard', 'Financing', 'Investment')`.
    - `CK_liability_payments_amount`: `amount > 0`.
    - Foreign key: `liability_payments.liability_id REFERENCES liabilities(id) ON DELETE RESTRICT`.
  - Migration: `20261006213014_026_Liabilities` created via:
    `dotnet ef migrations add 026_Liabilities --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
    applied via:
    `dotnet ef database update --context StoreOsDbContext --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
  - RLS: `tenant_isolation_policy` enabled and forced on both `liabilities` and `liability_payments` (`tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid`).
  - QueryFilters: tenant-level filters registered in `QueryFilters.ApplyAll`.
- Domain Tests (`StoreOS.Domain.Tests.FinanceOps`):
  - `LiabilityTests` (16 tests passing): valid creation of each liability type, invalid type throws, `UpdateDescriptiveData` strictly preserves `TotalPrincipal`, non-positive principal throws, empty title throws.
  - `LiabilityPaymentTests` (7 tests passing): valid creation, non-positive amount throws, empty payment mode throws, reflection verifies zero mutating methods on `LiabilityPayment`.
  - RLS coverage tests: `StoreOS.IntegrationTests.Schema.RlsCoverageTests` (5 tests passing).

## P-611
### Liabilities CRUD & Repayment Endpoints (Balance Derivation)
- Services:
  - `ILiabilityBalanceService` (`StoreOS.Application.FinanceOps`):
    - `Task<decimal> GetCurrentBalanceAsync(Guid liabilityId, CancellationToken ct)`
  - `LiabilityBalanceService` (`StoreOS.Infrastructure.FinanceOps`):
    - Injected with `StoreOsDbContext`.
    - Computes `CurrentBalance = Math.Max(0m, TotalPrincipal - SUM(LiabilityPayments.Amount))` live via query.
    - Scoped service registered in DI container.
  - Live-Balance Derivation Invariant: `CurrentBalance` is always live-derived and NEVER stored as a mutable column in `liabilities`.
- DTOs (`StoreOS.Application.FinanceOps.Liabilities`):
  - `LiabilityDto(Guid Id, string Title, string Type, string? LenderName, decimal TotalPrincipal, decimal CurrentBalance, DateOnly? DueDate, string? Status, string? Notes)`
- Application Vertical Slices (`StoreOS.Application.FinanceOps.Liabilities`):
  - `CreateLiability`:
    - Command: `CreateLiabilityCommand(string Title, string Type, string? LenderName, decimal TotalPrincipal, DateOnly? DueDate, string? Notes) : IRequest<Guid>, IRequiresPermission`
    - Permission: `liabilities:manage`
    - Validator: `CreateLiabilityValidator` (`liability_title_required`, `invalid_liability_type`, `invalid_principal`)
    - Handler: `CreateLiabilityHandler`
  - `GetLiabilities`:
    - Query: `GetLiabilitiesQuery(int Page = 1, int PageSize = 50) : IRequest<PagedResult<LiabilityDto>>, IReadOnlyRequest, IRequiresPermission`
    - Permission: `liabilities:manage`
    - Handler: `GetLiabilitiesHandler` (paginated list computing `CurrentBalance` via grouped payments)
  - `GetLiabilityById`:
    - Query: `GetLiabilityByIdQuery(Guid Id) : IRequest<LiabilityDto>, IReadOnlyRequest, IRequiresPermission`
    - Permission: `liabilities:manage`
    - Handler: `GetLiabilityByIdHandler` (fetches liability, live balance via `ILiabilityBalanceService`)
  - `UpdateLiability`:
    - Command: `UpdateLiabilityCommand(Guid Id, string Title, string? LenderName = null, DateOnly? DueDate = null, string? Status = null, string? Notes = null) : IRequest<Unit>, IRequiresPermission`
    - Permission: `liabilities:manage`
    - Validator: `UpdateLiabilityValidator` (`liability_id_required`, `liability_title_required`)
    - Handler: `UpdateLiabilityHandler` (calls `UpdateDescriptiveData` only; never mutates `TotalPrincipal`)
  - `RecordRepayment`:
    - Command: `RecordRepaymentCommand(Guid LiabilityId, decimal Amount, string PaymentMode, DateOnly PaymentDate, string? Notes = null) : IRequest<Guid>, IRequiresPermission`
    - Permission: `liabilities:manage`
    - Validator: `RecordRepaymentValidator` (`liability_id_required`, `invalid_payment_amount`, `payment_mode_required`, `payment_date_required`)
    - Handler: `RecordRepaymentHandler` (checks overpayment: throws `repayment_exceeds_balance` if amount > current balance; appends immutable `LiabilityPayment` row)
- Controller & Routes (`StoreOS.Api.Controllers.LiabilitiesController`):
  - `POST /api/liabilities` -> `Create(CreateLiabilityCommand)`
  - `GET /api/liabilities` -> `List(GetLiabilitiesQuery)`
  - `GET /api/liabilities/{id}` -> `GetById(Guid id)`
  - `PATCH /api/liabilities/{id}` -> `Update(Guid id, UpdateLiabilityBody)`
  - `POST /api/liabilities/{id}/repayments` -> `RecordRepayment(Guid id, RecordRepaymentBody)`
- Integration Tests (`StoreOS.IntegrationTests.FinanceOps.LiabilitiesTests`):
  - (a) Create liability with `TotalPrincipal=100000` -> initial `CurrentBalance=100000`.
  - (b) Repayment of `20000` -> `CurrentBalance=80000`.
  - (c) Second repayment of `30000` -> `CurrentBalance=50000`, verified two distinct rows in `liability_payments`.
  - (d) `PATCH /api/liabilities/{id}` updating title/lender leaves `TotalPrincipal` and `CurrentBalance` intact.
  - (e) Repayment of `50000.01` exceeding remaining balance throws `422` with `repayment_exceeds_balance`.
  - (f) Caller without `liabilities:manage` receives `403 Forbidden` on all five routes.

## P-701
### Repair Services CRUD & Query Endpoints (Catalog Overlay)
- Architecture Invariant:
  - Repair services are modeled as `products` rows with `category = 'RepairService'` (no new `repair_services` table, per Plan Section 8.4).
  - Never assigned branch inventory rows (`branch_inventory` invariant maintained).
- Domain Extensions (`StoreOS.Domain.Catalog.Product`):
  - Added `SetTaxable(bool isTaxable)` to mutate `IsTaxable`.
  - Added `SetHsnCode(string? hsnCode)` to mutate `HsnCode` (used as SAC for repair services).
- DTOs (`StoreOS.Application.Repairs.Services`):
  - `RepairServiceDto(Guid Id, string Name, string? Description, decimal StandardCharge, bool Taxable, decimal GstRate, string? SacCode, int? EstimatedDurationMinutes, int? WarrantyDays, bool IsActive)`
- Application Vertical Slices (`StoreOS.Application.Repairs.Services`):
  - `CreateRepairService`:
    - Command: `CreateRepairServiceCommand(string Name, string? Description, decimal StandardCharge, bool Taxable, decimal? GstRate, string? SacCode, int? EstimatedDurationMinutes, int? WarrantyDays) : IRequest<Guid>, IRequiresPermission`
    - Permission: `repairs:manage`
    - Validator: `CreateRepairServiceValidator`
    - Handler: `CreateRepairServiceHandler` (calls `Product.Create` with defaults: SAC `998713`, GST `18%`, duration `60`, warranty `30`)
  - `GetRepairServices`:
    - Query: `GetRepairServicesQuery(bool? IsActive = true, int Page = 1, int PageSize = 50) : IRequest<PagedResult<RepairServiceDto>>, IReadOnlyRequest`
    - Handler: `GetRepairServicesHandler` (filters `category == ProductCategory.RepairService`, defaults `IsActive = true` excluding inactive; passing `IsActive = false` includes inactive)
  - `GetRepairServiceById`:
    - Query: `GetRepairServiceByIdQuery(Guid Id) : IRequest<RepairServiceDto>, IReadOnlyRequest`
    - Handler: `GetRepairServiceByIdHandler`
  - `UpdateRepairService`:
    - Command: `UpdateRepairServiceCommand(Guid Id, string? Name = null, string? Description = null, decimal? StandardCharge = null, bool? Taxable = null, decimal? GstRate = null, string? SacCode = null, int? EstimatedDurationMinutes = null, int? WarrantyDays = null, bool? IsActive = null) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Validator: `UpdateRepairServiceValidator`
    - Handler: `UpdateRepairServiceHandler`
  - `DeactivateRepairService`:
    - Command: `DeactivateRepairServiceCommand(Guid Id) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `DeactivateRepairServiceHandler` (calls `product.Deactivate()`, preserving historical usage)
- Controller & Routes (`StoreOS.Api.Controllers.RepairServicesController`):
  - `GET /api/repair-services` -> `List(bool? isActive = true, int page = 1, int pageSize = 50)`
  - `GET /api/repair-services/{id}` -> `GetById(Guid id)`
  - `POST /api/repair-services` -> `Create(CreateRepairServiceCommand)`
  - `PATCH /api/repair-services/{id}` -> `Update(Guid id, UpdateRepairServiceBody)`
  - `POST /api/repair-services/{id}/deactivate` -> `Deactivate(Guid id)`
- Integration Tests (`StoreOS.IntegrationTests.Repairs.RepairServicesTests`):
  - (a) Creating repair service with minimal input gets defaults (SAC 998713, GST 18%, duration 60, warranty 30).
  - (b) Creating with explicit overrides respects them.
  - (c) `GET /api/repair-services` excludes inactive by default; `isActive=false` includes them.
  - (d) Deactivating service used in historical sale line leaves sale snapshot data unchanged.
  - (e) Creating repair service never creates a `branch_inventory` row.

## P-702
### Repair Jobs Entity, Migration & Job Number Sequence
- Domain Types (`StoreOS.Domain.Repairs`):
  - `RepairStatus`:
    - Constants: `Received`, `Diagnosing`, `WaitingForApproval`, `WaitingForParts`, `InRepair`, `ReadyForPickup`, `Delivered`, `Cancelled`
    - `All`: list of 8 statuses
    - `IsValid(string status) => All.Contains(status)`
    - `IsActive(string status) => status != Delivered && status != Cancelled`
  - `RepairJobNumberFormatter`:
    - `string Format(string branchCode, DateOnly date, int seq)` producing `REP-{branchCode}-{date:yyyyMMdd}-{seq:D4}`
  - `RepairJob` (Aggregate Root):
    - Properties: `Id`, `TenantId`, `BranchId`, `JobNumber`, `CustomerId`, `CustomerName`, `CustomerPhone`, `CustomerAddress`, `DeviceBrand`, `DeviceModel`, `DeviceType`, `SerialNumber`, `Imei1`, `Imei2`, `DeviceCondition`, `AccessoriesReceived`, `ProblemDescription`, `CustomerNotes`, `Diagnosis`, `TechnicianNotes`, `InternalNotes`, `Technician`, `EstimatedCost`, `ApprovedCost`, `PartsCost`, `LaborCharge`, `Discount`, `Tax`, `FinalTotal`, `AdvancePaid`, `BalanceDue`, `Status`, `ReceivedAt`, `ExpectedCompletion`, `CompletedAt`, `DeliveredAt`, `WarrantyUntil`, `ApprovalState`, `ApprovalTimestamp`, `CancellationReason`, `InvoiceNumber`, `SaleId`, `CreatedAt`, `UpdatedAt`.
    - Factory: `RepairJob Intake(...)` sets `Status = Received`, `ReceivedAt = UtcNow`, `CreatedAt = UtcNow`, `UpdatedAt = UtcNow`, and computes initial `BalanceDue` if estimated cost and advance paid are provided.
    - Methods:
      - `RecordDiagnosis(diagnosis, technicianNotes, internalNotes, technician)`
      - `SetCommercials(estimatedCost, approvedCost, partsCost, laborCharge, discount, tax, finalTotal, advancePaid)` auto-computing `BalanceDue = finalTotal - advancePaid`
      - `RecordApproval(approvalState, timestamp)`
      - `SetExpectedCompletion(dateTime)`
      - `Cancel(reason)` (throws `DomainValidationException` if job is `Delivered`)
      - `Deliver(deliveredAt, warrantyUntil)` (throws `DomainValidationException` if not in `ReadyForPickup`)
      - `LinkToSale(saleId, invoiceNumber)`
- Numbering Allocation:
  - Reuses `InvoiceSequenceAllocator` (from P-501) with series key `$"REP-{branchCode}-{date:yyyyMMdd}"`, atomic daily-resetting sequence.
- Entity Framework & Schema:
  - Table: `repair_jobs`
  - Migration: `20261007165937_027_RepairJobs`
  - Check constraint: `CK_repair_jobs_status` on 8 allowed statuses
  - Unique constraint: `ix_repair_jobs_tenant_job_number` on `(tenant_id, job_number)`
  - Indexes: `ix_repair_jobs_tenant_branch_status` on `(tenant_id, branch_id, status)` and `ix_repair_jobs_tenant_branch_customer` on `(tenant_id, branch_id, customer_id)`
  - RLS Archetype B: `tenant_branch_isolation_policy` enabled and forced.
  - QueryFilters: `rj.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(rj.BranchId))`
- Tests:
  - `RepairJobNumberFormatterTests` (2 passed): verifies format matches `REP-{branchCode}-{yyyyMMdd}-{XXXX}`.
  - `RepairJobTests` (6 passed): intake status/received_at, commercials balance_due, cancel delivered throws, deliver non-ready throws, deliver ready succeeds, active status definition.
  - `RepairJobNumberConcurrencyTests` (1 passed): 20 concurrent intakes generate strictly sequential and distinct job numbers.
  - `RlsCoverageTests` (5 passed): validates `repair_jobs` table RLS enabled, forced, and covered.

## P-703
### Repair Job CRUD & Intake/Diagnosis/Commercials Endpoints
- Authorization Decision:
  - `repairs:manage` is used uniformly for all repair job mutations (creation, general updates, diagnosis updates, commercials updates).
  - Read queries (`GetRepairJobs`, `GetRepairJobById`) require no special permission to support technician workflows.
- Domain Extensions (`StoreOS.Domain.Repairs.RepairJob`):
  - `UpdateIntakeDetails(customerNotes, problemDescription, deviceCondition, accessoriesReceived, expectedCompletion)`
- DTOs (`StoreOS.Application.Repairs.Jobs`):
  - `RepairJobDto(Guid Id, Guid TenantId, Guid BranchId, string JobNumber, Guid? CustomerId, string? CustomerName, string? CustomerPhone, string? CustomerAddress, string? DeviceBrand, string? DeviceModel, string? DeviceType, string? SerialNumber, string? Imei1, string? Imei2, string? DeviceCondition, string? AccessoriesReceived, string? ProblemDescription, string? CustomerNotes, string? Diagnosis, string? TechnicianNotes, string? InternalNotes, string? Technician, decimal? EstimatedCost, decimal? ApprovedCost, decimal? PartsCost, decimal? LaborCharge, decimal? Discount, decimal? Tax, decimal? FinalTotal, decimal? AdvancePaid, decimal? BalanceDue, string Status, DateTimeOffset? ReceivedAt, DateTimeOffset? ExpectedCompletion, DateTimeOffset? CompletedAt, DateTimeOffset? DeliveredAt, DateOnly? WarrantyUntil, string? ApprovalState, DateTimeOffset? ApprovalTimestamp, string? CancellationReason, string? InvoiceNumber, Guid? SaleId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)`
- Application Vertical Slices (`StoreOS.Application.Repairs.Jobs`):
  - `CreateRepairJob`:
    - Command: `CreateRepairJobCommand(...) : IRequest<Guid>, IRequiresPermission`
    - Permission: `repairs:manage`
    - Validator: `CreateRepairJobValidator`
    - Handler: `CreateRepairJobHandler` (allocates sequence via `InvoiceSequenceAllocator` and formats via `RepairJobNumberFormatter`)
  - `GetRepairJobs`:
    - Query: `GetRepairJobsQuery(string? Status = null, string? Search = null, int Page = 1, int PageSize = 50) : IRequest<PagedResult<RepairJobDto>>, IReadOnlyRequest`
    - Handler: `GetRepairJobsHandler` (supports "Active" filter, individual status filters, search by model/serial/imei1/phone/customer/job_number)
  - `GetRepairJobById`:
    - Query: `GetRepairJobByIdQuery(Guid Id) : IRequest<RepairJobDto>, IReadOnlyRequest`
    - Handler: `GetRepairJobByIdHandler`
  - `UpdateRepairJob`:
    - Command: `UpdateRepairJobCommand(Guid Id, string? CustomerNotes, string? ProblemDescription, string? DeviceCondition, string? AccessoriesReceived, DateTimeOffset? ExpectedCompletion) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `UpdateRepairJobHandler`
  - `UpdateRepairJobDiagnosis`:
    - Command: `UpdateRepairJobDiagnosisCommand(Guid Id, string? Diagnosis, string? TechnicianNotes, string? InternalNotes, string? Technician) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `UpdateRepairJobDiagnosisHandler`
  - `UpdateRepairJobCommercials`:
    - Command: `UpdateRepairJobCommercialsCommand(Guid Id, decimal? EstimatedCost, decimal? ApprovedCost, decimal? PartsCost, decimal? LaborCharge, decimal? Discount, decimal? Tax, decimal? FinalTotal, decimal? AdvancePaid) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `UpdateRepairJobCommercialsHandler` (calls `job.SetCommercials(...)` which auto-recalculates `BalanceDue`)
- Controller & Routes (`StoreOS.Api.Controllers.RepairJobsController`):
  - `GET /api/repair-jobs` -> `List(GetRepairJobsQuery)`
  - `GET /api/repair-jobs/{id}` -> `GetById(Guid id)`
  - `POST /api/repair-jobs` -> `Create(CreateRepairJobCommand)`
  - `PATCH /api/repair-jobs/{id}` -> `Update(Guid id, UpdateRepairJobBody)`
  - `PATCH /api/repair-jobs/{id}/diagnosis` -> `UpdateDiagnosis(Guid id, UpdateRepairJobDiagnosisBody)`
  - `PATCH /api/repair-jobs/{id}/commercials` -> `UpdateCommercials(Guid id, UpdateRepairJobCommercialsBody)`
- Integration Tests (`StoreOS.IntegrationTests.Repairs.RepairJobsCrudTests`):
  - (a) Job creation allocates unique, formatted job number (`REP-B1-YYYYMMDD-XXXX`) and sets `Status=Received`.
  - (b) Search by device model, IMEI1, and customer phone works.
  - (c) Status filter `Active` returns only jobs not in `Delivered` or `Cancelled`.
  - (d) Updating diagnosis and commercials persists correctly and recomputes `BalanceDue`.
  - (e) Caller without `repairs:manage` receives 403 Forbidden on mutations, but can read via list and detail queries.

## P-704
### Repair Job Status History Entity, State Machine & Status Endpoints
- Domain Types (`StoreOS.Domain.Repairs`):
  - `RepairStatusTransitions`:
    - Terminal states: `Delivered`, `Cancelled` (never transition further).
    - Non-terminal states: `Received`, `Diagnosing`, `WaitingForApproval`, `WaitingForParts`, `InRepair`, `ReadyForPickup`.
    - Rules:
      - Terminal states cannot transition to anything (`from in Terminal => false`).
      - `Delivered` reachable only from `ReadyForPickup`.
      - `Cancelled` reachable from any non-terminal state.
      - Transitions between any distinct non-terminal states are fully permitted in either direction.
      - `IsLegal(string from, string to)` evaluates the rule set.
  - `RepairJobStatusHistory` (Append-only Entity):
    - Columns: `Id` (bigint identity), `TenantId` (uuid), `RepairJobId` (uuid FK), `FromStatus` (text), `ToStatus` (text), `Notes` (text), `ChangedBy` (uuid), `ChangedAt` (timestamptz).
    - Factory: `RepairJobStatusHistory.Create(tenantId, repairJobId, fromStatus, toStatus, notes, changedBy)`.
  - `RepairJob` Entity Extension:
    - `TransitionTo(string newStatus)` validates legal transition via `RepairStatusTransitions.IsLegal` (throws `DomainValidationException` on illegal), sets `Status`, sets `DeliveredAt = UtcNow` if reaching `Delivered`, updates `UpdatedAt`.
- Database & Migration:
  - Table: `repair_job_status_history`
  - Migration: `20261007171447_028_RepairStatusHistory`
  - RLS Archetype A: `tenant_isolation_policy` enabled and forced.
  - QueryFilters: `ApplyFilter<RepairJobStatusHistory>(modelBuilder, scopeAccess, (h, s) => h.TenantId == s.TenantId)`.
- Application Vertical Slices (`StoreOS.Application.Repairs.Jobs`):
  - `ChangeStatus`:
    - Command: `ChangeRepairStatusCommand(Guid RepairJobId, string NewStatus, string? Notes = null) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Validator: `ChangeRepairStatusValidator`
    - Handler: `ChangeRepairStatusHandler` (transitions job, records history entry atomically)
  - `GetStatusHistory`:
    - Query: `GetRepairStatusHistoryQuery(Guid RepairJobId) : IRequest<IReadOnlyList<RepairStatusHistoryDto>>, IReadOnlyRequest`
    - DTO: `RepairStatusHistoryDto(long Id, Guid RepairJobId, string? FromStatus, string ToStatus, string? Notes, Guid? ChangedBy, DateTimeOffset ChangedAt)`
    - Handler: `GetRepairStatusHistoryHandler`
- Controller & Routes (`StoreOS.Api.Controllers.RepairJobsController`):
  - `POST /api/repair-jobs/{id}/status` -> `ChangeStatus(Guid id, ChangeRepairStatusBody body)`
  - `GET /api/repair-jobs/{id}/status-history` -> `GetStatusHistory(Guid id)`
- Tests:
  - `RepairStatusTransitionsTests` (24 passed): verifies all legal transitions pass and illegal transitions fail (terminal states, premature delivered, same-state loops).
  - `ChangeRepairStatusTests` (4 passed): legal transition creates history row; illegal transition fails 422 with zero history rows; delivered transition sets `DeliveredAt`; sequential transitions create ordered history trail.
  - `RlsCoverageTests` (5 passed): validates `repair_job_status_history` table RLS enabled, forced, and covered.

## P-705
### Repair Job Parts Entity, Migration & Stock Reservation/Consumption/Return
- Domain Types (`StoreOS.Domain.Repairs`):
  - `RepairPartStatus`:
    - Constants: `Reserved`, `Consumed`, `Returned`
    - `All`: list of 3 statuses
    - `IsValid(string status) => All.Contains(status)`
  - `RepairJobPart` Entity:
    - Columns: `Id` (uuid PK), `TenantId` (uuid), `RepairJobId` (uuid FK), `ProductId` (uuid nullable), `PartName` (text), `Quantity` (int > 0), `UnitCost` (numeric 18,2), `ChargeAmount` (numeric 18,2), `Status` (text), `CreatedAt` (timestamptz).
    - Factory: `RepairJobPart.Reserve(tenantId, repairJobId, productId, partName, quantity, unitCost, chargeAmount)`
    - Methods:
      - `MarkConsumed()` (guards: only from `Reserved`, throws `part_not_reserved`)
      - `MarkReturned()` (guards: cannot return if already `Returned`, throws `part_already_returned`)
- Stock Lifecycle Invariant:
  - Reserving a part NEVER decrements inventory stock (soft reservation).
  - Consuming a tracked part (`ProductId != null`) calls `IInventoryStockService.ReserveAndDecrementStockAsync` with `MovementType.RepairConsumed`. Fails atomically on insufficient stock.
  - Returning a consumed tracked part calls `IInventoryStockService.IncreaseStockAsync` with `MovementType.RepairPartReturned`.
  - Returning a reserved part (never consumed) does not move stock.
  - Untracked parts (`ProductId == null`) never interact with `IInventoryStockService`.
- Database & Migration:
  - Table: `repair_job_parts`
  - Migration: `20261007172323_029_RepairJobParts` (Next sequential: 029)
  - Constraints: `CK_repair_job_parts_quantity` (`quantity > 0`), `CK_repair_job_parts_status` (`Reserved`, `Consumed`, `Returned`), FK to `repair_jobs`.
  - RLS Archetype A: `tenant_isolation_policy` enabled and forced.
  - QueryFilters: `ApplyFilter<RepairJobPart>(modelBuilder, scopeAccess, (p, s) => p.TenantId == s.TenantId)`.
- Application Vertical Slices (`StoreOS.Application.Repairs.Parts`):
  - `ReservePart`:
    - Command: `ReservePartCommand(Guid RepairJobId, Guid? ProductId, string PartName, int Quantity, decimal UnitCost, decimal ChargeAmount) : IRequest<Guid>, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `ReservePartHandler` (creates `RepairJobPart` in `Reserved` status, zero stock movement)
  - `ConsumePart`:
    - Command: `ConsumePartCommand(Guid RepairJobId, Guid PartId) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `ConsumePartHandler` (decrements stock via `_stockService.ReserveAndDecrementStockAsync` if `ProductId` present, marks `Consumed`)
  - `ReturnPart`:
    - Command: `ReturnPartCommand(Guid RepairJobId, Guid PartId) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Handler: `ReturnPartHandler` (reverses stock via `_stockService.IncreaseStockAsync` if consumed tracked part, marks `Returned`)
  - `GetRepairJobParts`:
    - Query: `GetRepairJobPartsQuery(Guid RepairJobId) : IRequest<IReadOnlyList<RepairJobPartDto>>, IReadOnlyRequest`
    - Handler: `GetRepairJobPartsHandler`
- Controller & Routes (`StoreOS.Api.Controllers.RepairJobsController`):
  - `GET /api/repair-jobs/{id}/parts` -> `GetParts(Guid id)`
  - `POST /api/repair-jobs/{id}/parts` -> `ReservePart(Guid id, ReserveRepairPartBody body)`
  - `POST /api/repair-jobs/{id}/parts/{partId}/consume` -> `ConsumePart(Guid id, Guid partId)`
  - `POST /api/repair-jobs/{id}/parts/{partId}/return` -> `ReturnPart(Guid id, Guid partId)`
- Integration Tests (`StoreOS.IntegrationTests.Repairs.RepairPartsTests`):
  - (a) Reserving tracked part does not decrement stock.
  - (b) Consuming decrements stock atomically, and fails atomically with `insufficient_stock` rolling back.
  - (c) Returning consumed part restores stock.
  - (d) Returning reserved part (never consumed) does not move stock.
  - (e) Untracked part lifecycle succeeds without stock service interaction.
  - (f) Consuming already-consumed part throws `part_not_reserved`.
  - `RlsCoverageTests` (5 passed): validates `repair_job_parts` table RLS enabled, forced, and covered.

## P-706
### Customer Approval & Warranty Delivery Endpoints
- Domain Types (`StoreOS.Domain.Repairs`):
  - `ApprovalState`:
    - Constants: `Pending`, `Approved`, `Rejected`
    - `All`: list of 3 states
    - `IsValid(string state) => All.Contains(state)`
  - `RepairJob`:
    - Method `RecordApproval(string? approvalState, DateTimeOffset? timestamp = null)` sets `ApprovalState` and `ApprovalTimestamp`.
    - Method `Deliver(DateTimeOffset deliveredAt, DateOnly? warrantyUntil)`:
      - Guards: `Status == ReadyForPickup || Status == Delivered` (throws `invalid_status_for_delivery` with descriptive message if neither).
      - Sets `DeliveredAt = deliveredAt`, `WarrantyUntil = warrantyUntil`, `Status = Delivered`.
      - Authoritative setter for `DeliveredAt` and `WarrantyUntil` timestamps; `TransitionTo` sets `Status` and validates transition via `RepairStatusTransitions`.
- Application Vertical Slices (`StoreOS.Application.Repairs`):
  - `RecordApproval`:
    - Command: `RecordApprovalCommand(Guid RepairJobId, string ApprovalState) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Validator: `RecordApprovalValidator` validates `ApprovalState` is non-empty and in `ApprovalState.All` (400 on invalid).
    - Handler: `RecordApprovalHandler` loads job and calls `job.RecordApproval(request.ApprovalState, DateTimeOffset.UtcNow)`.
  - `DeliverRepairJob`:
    - Command: `DeliverRepairJobCommand(Guid RepairJobId, int? WarrantyDaysOverride = null) : IRequest, IRequiresPermission`
    - Permission: `repairs:manage`
    - Validator: `DeliverRepairJobValidator` validates `WarrantyDaysOverride >= 0` if specified.
    - Handler: `DeliverRepairJobHandler`:
      - Calls `job.TransitionTo(RepairStatus.Delivered)` (validates status transition, throws `illegal_status_transition` 422 if not from `ReadyForPickup`).
      - Computes warranty: `request.WarrantyDaysOverride ?? (single linked repair service Product.WarrantyDays ?? 30)`.
      - Calls `job.Deliver(now, warrantyUntil)`.
      - Appends `RepairJobStatusHistory` audit row for `Delivered`.
- Controller & Routes (`StoreOS.Api.Controllers.RepairJobsController`):
  - `POST /api/repair-jobs/{id}/approval` -> `RecordApproval(Guid id, RecordApprovalBody body)`
  - `POST /api/repair-jobs/{id}/deliver` -> `Deliver(Guid id, DeliverRepairJobBody? body)`
- Integration Tests (`StoreOS.IntegrationTests.Repairs.ApprovalAndDeliveryTests`):
  - (a) Invalid `ApprovalState` -> 400; valid `ApprovalState` persists with timestamp.
  - (b) Delivering a job not in `ReadyForPickup` -> 422 `illegal_status_transition`.
  - (c) Delivering a job linked to a repair-service product (`WarrantyDays=45`) -> `WarrantyUntil` is exactly 45 days.
  - (d) Delivering with no override and no single linked service -> falls back to 30 days tenant default.
  - (e) `DeliveredAt` is set accurately with no conflicting double-write between `TransitionTo` and `Deliver`.

## P-707
### Repair Billing: Two-Step FK Resolution Linking Repair Job to Sale
- Interfaces & Services:
  - `StoreOS.Application.Repairs.IRepairBillingService`:
    `Task LinkSaleAsync(Guid repairJobId, Guid saleId, string invoiceNumber, CancellationToken ct);`
  - `StoreOS.Infrastructure.Repairs.RepairBillingService`:
    Implementation calling `job.LinkToSale(saleId, invoiceNumber)`.
    Registered scoped in `StoreOS.Api/Program.cs`.
- Two-Step FK Resolution Order:
  - Step 1: Create and insert `Sale` first via `Sale.CreateTaxInvoice(...)` with `RepairJobId = repairJobId` populated directly at construction time.
  - Step 2: Update `RepairJob` in the same transaction via `IRepairBillingService.LinkSaleAsync(...)` setting `SaleId = sale.Id` and `InvoiceNumber = invoiceNumber`.
  - All changes committed atomically in a single `SaveChangesAsync()` call. Avoids mutual FK insertion deadlocks and circular dependency.
- Application Vertical Slice (`StoreOS.Application.Repairs.BillRepair`):
  - `BillRepairCommand(Guid RepairJobId, IReadOnlyList<CheckoutPayment>? Payments = null) : IRequest<BillRepairResult>, IRequiresPermission, IIdempotentRequest`
    - Permission: `repairs:manage`
    - IdempotencyScope: `repairs.bill:{RepairJobId}`
    - IdempotencyRequired: `true`
  - `BillRepairResult(Guid SaleId, string InvoiceNumber, decimal GrandTotal, decimal AmountPaid, decimal Balance)`
  - `BillRepairValidator`:
    - Validates `RepairJobId` is non-empty.
    - Validates payment amounts non-negative and mode non-empty.
  - `BillRepairHandler`:
    - Guards: `job.FinalTotal != null` (throws `repair_commercials_not_set` 422 if null); `job.SaleId == null` (throws `repair_job_already_billed` 422 if already billed).
    - Constructs `SaleItem` with `PricePerUnit = job.FinalTotal`.
    - Allocates invoice number via `IInvoiceSequenceAllocator` (`FYxx` consecutive series).
    - Composes payments via `PaymentComposer` and posts debit/credit to customer ledger via `ICustomerLedgerService`.
    - Links repair job and records audit log.
- Controller & Routes (`StoreOS.Api.Controllers.RepairJobsController`):
  - `POST /api/repair-jobs/{id}/bill` -> `BillRepair(Guid id, BillRepairBody? body, string? idempotencyKey, IdempotencyReplayFlag replayFlag)`
  - Requires header `Idempotency-Key` (400 `idempotency_key_required` if absent).
- Integration Tests (`StoreOS.IntegrationTests.Repairs.BillRepairTests`):
  - (a) Billing repair job with commercials set creates `Sale` with `RepairJobId` and sets `RepairJob.SaleId`/`InvoiceNumber` (bidirectional link verified both directions).
  - (b) Billing job with no commercials set returns 422 `repair_commercials_not_set`.
  - (c) Created sale's `GrandTotal` matches `job.FinalTotal`.
  - (d) Caller without `repairs:manage` returns 403 Forbidden.
  - (e) Replaying identical `Idempotency-Key` returns cached result without double billing (headers contain `Idempotent-Replayed: true`).
  - (f) 10-iteration loop assertion confirms zero deadlock and zero FK-constraint failures under repeated execution.

## P-708
### Repair Feature Flag (IFeatureManager & RepairsFeatureGateBehavior)
- Interfaces & Marker:
  - `StoreOS.Application.Platform.IFeatureManager`:
    - `Task<bool> IsRepairsEnabledAsync(CancellationToken ct);`
    - `Task SetRepairsEnabledAsync(bool enabled, CancellationToken ct);`
  - `StoreOS.Infrastructure.Platform.TenantFeatureManager`:
    - Reads/writes `tenants.features` JSONB (`{"repairs": true|false}`).
    - Registered scoped in `StoreOS.Api/Program.cs`.
  - `StoreOS.Application.Abstractions.IRequiresRepairsFeature`:
    - Marker interface gating repair commands and queries.
- MediatR Pipeline Behavior:
  - `StoreOS.Application.Behaviors.RepairsFeatureGateBehavior<TRequest, TResponse>`:
    - Intercepts requests implementing `IRequiresRepairsFeature`.
    - If `!await _featureManager.IsRepairsEnabledAsync(ct)`, throws `DomainValidationException("repairs_module_disabled", "The repairs module is not enabled for this tenant.")` (mapped to 422 Unprocessable Entity).
    - Registered in MediatR pipeline after `TransactionBehavior`/`LiveDbPermissionBehavior`.
- Retrofitted Repair Use-Cases (all implementing `IRequiresRepairsFeature`):
  1. `CreateRepairServiceCommand`
  2. `GetRepairServicesQuery`
  3. `GetRepairServiceByIdQuery`
  4. `UpdateRepairServiceCommand`
  5. `DeactivateRepairServiceCommand`
  6. `CreateRepairJobCommand`
  7. `GetRepairJobsQuery`
  8. `GetRepairJobByIdQuery`
  9. `UpdateRepairJobDiagnosisCommand`
  10. `UpdateRepairJobCommercialsCommand`
  11. `UpdateRepairJobCommand`
  12. `ChangeRepairStatusCommand`
  13. `GetRepairStatusHistoryQuery`
  14. `ReservePartCommand`
  15. `ConsumePartCommand`
  16. `ReturnPartCommand`
  17. `GetRepairJobPartsQuery`
  18. `RecordApprovalCommand`
  19. `DeliverRepairJobCommand`
  20. `BillRepairCommand`
- Settings Endpoints & Slices (`StoreOS.Application.Settings.RepairFeature`):
  - `GetRepairsEnabledQuery : IRequest<bool>, IReadOnlyRequest`
  - `SetRepairsEnabledCommand(bool Enabled) : IRequest, IRequiresPermission` (`settings:manage`)
  - Routes on `SettingsController`:
    - `GET /api/settings/repairs-enabled` -> `GetRepairsEnabled`
    - `PATCH /api/settings/repairs-enabled` -> `SetRepairsEnabled`
- Multi-Tenancy & Query Filters:
  - `QueryFilters.ApplyAll`: added `ApplyFilter<Tenant>(modelBuilder, scopeAccess, (t, s) => s.TenantId == Guid.Empty || t.Id == s.TenantId)` ensuring post-auth tenant reads are scoped to current tenant while unauthenticated resolution and fixtures (`NullAccessScope`) function seamlessly.
  - `TenantResolver`: uses `IgnoreQueryFilters()` for pre-auth subdomain/id resolution.
- Integration Tests (`StoreOS.IntegrationTests.Repairs.RepairsFeatureFlagTests`):
  - (a) Feature disabled by default: all 20 repair endpoints return 422 `repairs_module_disabled`.
  - (b) Enabling feature via `PATCH /api/settings/repairs-enabled` immediately activates repair operations.
  - (c) Disabling feature gates API operations without deleting or altering existing database rows (jobs, parts, history intact).

## P-801
### Dashboard Aggregate Query (ReportingDbContext)
- Query and DTOs (`StoreOS.Application.Reporting.Dashboard`):
  - `GetDashboardQuery(Guid[]? BranchIds = null) : IRequest<DashboardDto>, IReportingRequest`
  - `DashboardDto(decimal TodayRevenue, int TodayInvoiceCount, decimal MonthlyProfit, int ActiveRepairsCount, int ReadyForPickupCount, IReadOnlyList<LowStockItemDto> LowStockItems, IReadOnlyList<TopSellingItemDto> TopSellingItems, IReadOnlyList<DailyRevenuePointDto> SevenDayRevenue)`
  - `LowStockItemDto(Guid ProductId, string Name, int StockQty, int Threshold)`
  - `TopSellingItemDto(string ItemName, int TotalQtySold)`
  - `DailyRevenuePointDto(DateOnly Date, decimal Revenue)`
- Handler (`StoreOS.Application.Reporting.Dashboard.GetDashboardHandler`):
  - Implements `IRequestHandler<GetDashboardQuery, DashboardDto>` using `IReportingDbContext`, `IAccessScope`, and `IFeatureManager`.
  - Timezone validation: resolves tenant timezone via `IAccessScope.TenantId`, supporting `Asia/Kolkata` (`India Standard Time` fallback on Windows) and throwing `unsupported_timezone` for any other configured timezone.
  - Today's Revenue & Invoice Count: calculates `SUM(grand_total)` and `COUNT(*)` strictly for `invoice_type = 'TaxInvoice'` and `status = 'Active'` within today's business date range in Asia/Kolkata, explicitly excluding `Voided`, `Refunded`, and `Estimate` invoices.
  - Monthly Profit: `Revenue - COGS - OperationalExpenses` for the current calendar month. Revenue sums `taxable + cgst + sgst` across Active TaxInvoices excluding gift lines (`!is_gift`). COGS sums `qty * purchase_price_snapshot` for the same lines (strictly using immutable historical snapshot `purchase_price_snapshot`, never current `products.purchase_price`). Operational expenses sum `expenses.amount` for the current month matched to `BranchIds`.
  - Active Repairs & Ready for Pickup: gated by `IFeatureManager.IsRepairsEnabledAsync(ct)`. Returns 0 for both if repairs module is disabled for the tenant. When enabled, counts active jobs (`status != Delivered && status != Cancelled`) and ready for pickup jobs (`status == ReadyForPickup`).
  - Low-Stock Alerts: queries `stock_qty > 0 AND stock_qty <= low_stock_threshold`, defensively excluding `RepairService` category.
  - Top-Selling Items: aggregates top 5 items by `SUM(qty)` for current calendar month where `is_gift = false` across Active TaxInvoices.
  - Seven-Day Revenue Chart: 7 daily revenue points ending on today in business dates, with zero revenue explicitly populated for days without sales.
- API Route (`StoreOS.Api.Controllers.ReportsController`):
  - `GET /api/reports/dashboard?branchIds=` -> `GetDashboard(Guid[]? branchIds, CancellationToken ct)`
- Integration Tests (`StoreOS.IntegrationTests.Reporting.DashboardTests`):
  - All 6 tests verified against PostgreSQL testcontainer:
    - (a) Today's revenue and invoice count explicitly exclude Voided, Refunded, and Estimate sales.
    - (b) Monthly profit uses `purchase_price_snapshot`, proven by modifying product `purchase_price` after sale with zero change to reported monthly profit.
    - (c) Low-stock list matches exact boundary (`stock_qty > 0 AND stock_qty <= low_stock_threshold`).
    - (d) Top-5 sellers excludes gift line quantities.
    - (e) Seven-day revenue chart includes zero-revenue days explicitly.
    - (f) Active repairs and ready for pickup return 0 when repairs feature is disabled, and accurately return counts when enabled.

## P-802
### Daily Sales Audit Report (ReportingDbContext)
- Query and DTOs (`StoreOS.Application.Reporting.DailySales`):
  - `GetDailySalesReportQuery(DateOnly Date, Guid[]? BranchIds = null) : IRequest<DailySalesReportDto>, IReportingRequest`
  - `DailySalesReportDto(DateOnly Date, SalesSummaryDto Sales, InventorySummaryDto Inventory, PaymentBreakdownDto Payments, IReadOnlyList<CategorySalesDto> ByCategory, IReadOnlyList<ItemizedSaleLineDto> ItemizedLog, decimal CollectedAmount, decimal OutstandingAmount)`
  - `SalesSummaryDto(decimal Revenue, decimal BuyingCost, decimal Profit, decimal GstCollected, int InvoiceCount, int ItemsSold)`
  - `InventorySummaryDto(int TotalStockVolume, decimal StockWorthAtCost, decimal RetailValuation, int LowStockCount, int OutOfStockCount)`
  - `PaymentBreakdownDto(decimal Cash, decimal Upi, decimal Finance, decimal Other, decimal Total)`
  - `CategorySalesDto(string Category, decimal Revenue, int ItemsSold)`
  - `ItemizedSaleLineDto(string InvoiceNumber, DateOnly Date, string CustomerName, string ItemName, int Qty, decimal SellingValue, decimal CostValue, decimal Gst, string? Imei, string? PaymentMode)`
- Handler (`StoreOS.Application.Reporting.DailySales.GetDailySalesReportHandler`):
  - Implements `IRequestHandler<GetDailySalesReportQuery, DailySalesReportDto>` using `IReportingDbContext` and `IAccessScope`.
  - Timezone & Date Range: resolves tenant timezone via `IAccessScope.TenantId` (Asia/Kolkata), computing exact UTC boundaries for the business date.
  - Sales Summary: calculates `Revenue` (Active TaxInvoices `SUM(grand_total)`), `BuyingCost` (using historical snapshot `purchase_price_snapshot` exclusively, excluding gift lines), `Profit = Revenue - BuyingCost`, `GstCollected` (`SUM(cgst + sgst)`), `InvoiceCount`, and `ItemsSold` (`SUM(qty)` strictly excluding non-revenue gift lines).
  - Inventory Summary: reuses `ValuationCalculator.Calculate` for non-RepairService lines as of report runtime across requested branches, computing `TotalStockVolume`, `StockWorthAtCost`, `RetailValuation`, `LowStockCount` (`stock_qty > 0 AND stock_qty <= low_stock_threshold`), and `OutOfStockCount` (`stock_qty <= 0`).
  - Payment Breakdown & Dual Interpretation:
    - Buckets payments recorded on `Date` into `Cash`, `UPI`, `Finance`, and `Other` (all other modes including `Card` and `Credit`), with `Total = Cash + UPI + Finance + Other`.
    - `CollectedAmount` = broader same-day collected figure (total payments recorded today across all sales, including installments on older sales).
    - `OutstandingAmount` = narrower same-day-sale figure = `Revenue - CollectedForTodaySales` specifically for today's active invoices.
  - Sales by Category: groups line-item revenue and items sold across categories, covering at minimum `NewPhone`, `UsedPhone`, `Accessory`, and `RepairService` with zero-fill for categories with no sales that day.
  - Itemized Sales Log: provides granular audit lines for every item sold with invoice number, date, customer name, item name, quantity, selling value, cost value (snapshot), GST, IMEI, and payment mode.
- API Route (`StoreOS.Api.Controllers.ReportsController`):
  - `GET /api/reports/daily-sales?date=&branchIds=` -> `GetDailySales(DateOnly? date, Guid[]? branchIds, CancellationToken ct)` (defaults to current date if omitted).
- Integration Tests (`StoreOS.IntegrationTests.Reporting.DailySalesReportTests`):
  - All 6 tests verified against PostgreSQL testcontainer:
    - (a) Sales summary profit uses snapshot cost (proven unchanged after modifying product master purchase price) and excludes gift lines from items sold count.
    - (b) Inventory summary excludes RepairService products.
    - (c) Payment breakdown correctly buckets Card and Credit into Other.
    - (d) Sale-value vs. collected vs. outstanding dual interpretation verified with exact mathematical precision.
    - (e) Category breakdown zero-fills categories with no sales.
    - (f) Itemized log includes every line with correct IMEI and payment mode detail.

## P-803
### Multi-Branch Comparison Report + Consolidated Totals
- Shared Reporting Helper (`StoreOS.Application.Reporting.ReportAggregationHelpers`):
  - `public record SalesMetrics(decimal Revenue, decimal BuyingCost, decimal Profit, decimal GstCollected, int InvoiceCount, int ItemsSold)`
  - `public static SalesMetrics ComputeSalesMetrics(IEnumerable<Sale> sales, IEnumerable<SaleItem> lines)`
  - `public static int CountActiveRepairs(IEnumerable<RepairJob> jobs, bool isRepairsEnabled)`
  - Centralizes core sales and repairs aggregation across `GetDailySalesReportHandler` and `GetBranchComparisonHandler`, eliminating logic drift.
- Query and DTOs (`StoreOS.Application.Reporting.Compare`):
  - `GetBranchComparisonQuery(DateOnly Date) : IRequest<BranchComparisonDto>, IReportingRequest`
  - `BranchRowDto(Guid BranchId, string BranchName, decimal Revenue, int InvoiceCount, decimal Profit, int ActiveRepairs)`
  - `BranchComparisonDto(IReadOnlyList<BranchRowDto> Branches, BranchRowDto Consolidated)`
- Handler (`StoreOS.Application.Reporting.Compare.GetBranchComparisonHandler`):
  - Implements `IRequestHandler<GetBranchComparisonQuery, BranchComparisonDto>` using `IReportingDbContext`, `IAccessScope`, and `IFeatureManager`.
  - Access Scope Scoping: resolves all branches accessible to caller via `IAccessScope.AllBranches` and `IAccessScope.BranchIds`. Single-branch users receive only their single branch row and an identical `Consolidated` row.
  - Core Calculation: computes `Revenue`, `InvoiceCount`, `Profit` (using historical `purchase_price_snapshot`), and `ActiveRepairs` (gated by `IFeatureManager`) per branch.
  - Consolidated Row Invariant: `Consolidated.Revenue == sum(Branches.Revenue)`, `Consolidated.InvoiceCount == sum(Branches.InvoiceCount)`, `Consolidated.Profit == sum(Branches.Profit)`, `Consolidated.ActiveRepairs == sum(Branches.ActiveRepairs)`.
- API Route (`StoreOS.Api.Controllers.ReportsController`):
  - `GET /api/reports/compare?date=` -> `GetBranchComparison(DateOnly? date, CancellationToken ct)` (defaults to current date if omitted).
- Integration Tests (`StoreOS.IntegrationTests.Reporting.BranchComparisonTests`):
  - (a) Multi-branch user querying date with sales across 2 branches sees exact per-branch rows.
  - (b) Consolidated row exactly equals the sum of individual branch rows for every metric.
  - (c) Single-branch user sees only their accessible branch row with matching consolidated row.
  - (d) Regression suite passed: `DailySalesReportTests` verified green following the `ReportAggregationHelpers` refactor.

## P-804
### Inventory Export Endpoint (Physical and Service Separation)
- Query and DTOs (`StoreOS.Application.Reporting.InventoryExport`):
  - `GetInventoryExportQuery(string[] Categories, Guid[]? BranchIds = null) : IRequest<InventoryExportDto>, IReportingRequest`
  - `PhysicalInventoryExportRowDto(string Item, string Category, int Quantity, decimal PurchasePrice, decimal SellingPrice, decimal Gst, string? Hsn, string? Supplier, string? ImeiSummary)`
  - `ServiceExportRowDto(string Service, string? Description, decimal ServiceCharge, bool Taxable, decimal Gst, string? Sac)`
  - `InventoryExportDto(IReadOnlyList<PhysicalInventoryExportRowDto> PhysicalItems, IReadOnlyList<ServiceExportRowDto> Services)`
- CSV Writer Helper (`StoreOS.Application.Reporting.InventoryExport.InventoryCsvWriter`):
  - `public static string ToCsv(InventoryExportDto export, bool includePurchasePrice = true)`
  - `public static string Escape(string value)` (RFC 4180 quoting for commas, quotes, and newlines)
  - Emits UTF-8 BOM preamble and separates physical items and services into distinct sections with appropriate column headers.
- Handler (`StoreOS.Application.Reporting.InventoryExport.GetInventoryExportHandler`):
  - Implements `IRequestHandler<GetInventoryExportQuery, InventoryExportDto>` using `IReportingDbContext` and `IAccessScope`.
  - Strict Two-List Separation: physical categories populate `PhysicalItems` and `RepairService` populates `Services`, never mixing rows into a unified shape.
  - Permission Gating: `PurchasePrice` is masked to `0m` when the caller lacks `inventory:cost:view`.
  - IMEI Aggregation: active IMEIs listed up to 10 with a `+N more` suffix beyond 10.
  - Supplier Resolution: resolved from latest purchase record for each product.
- API Route (`StoreOS.Api.Controllers.ExportsController`):
  - `GET /api/exports/inventory?categories=&branchIds=&format=csv|json` -> `ExportInventory`
  - Emits CSV with UTF-8 BOM (`[0xEF, 0xBB, 0xBF]`) and `Content-Disposition: attachment; filename="inventory-<yyyyMMdd>.csv"` by default, or JSON when requested via `format=json` or `Accept: application/json`.
- Integration Tests (`StoreOS.IntegrationTests.Reporting.InventoryExportTests`):
  - (a) Requesting all 4 categories returns physical items in `PhysicalItems` and services in `Services` without mixing.
  - (b) Requesting only `["RepairService"]` returns empty `PhysicalItems` and populated `Services`.
  - (c) IMEI truncation verified for items with >10 active IMEIs (`+N more` suffix).
  - (d) Requesting category subset (`["Accessory"]`) returns only physical items of that category.
  - (e) CSV format export verified for UTF-8 BOM, RFC 4180 headers, and Content-Disposition header.
  - (f) Caller lacking `inventory:cost:view` permission receives `PurchasePrice == 0m`.

## P-805
### GSTR-1 Export Grouped by Branch GSTIN
- Query and DTOs (`StoreOS.Application.Reporting.Gstr1Export`):
  - `GetGstr1ExportQuery(DateOnly From, DateOnly To) : IRequest<Gstr1ExportDto>, IReportingRequest`
  - `Gstr1RowDto(string InvoiceNumber, DateOnly Date, string CustomerName, string? CustomerGstin, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal TotalValue, string? HsnOrSac, string Category)`
  - `Gstr1GroupDto(string Gstin, IReadOnlyList<Gstr1RowDto> Rows, decimal TotalTaxable, decimal TotalCgst, decimal TotalSgst, decimal GrandTotal)`
  - `Gstr1ExportDto(IReadOnlyList<Gstr1GroupDto> ByGstin)`
- CSV Writer Helper (`StoreOS.Application.Reporting.Gstr1Export.Gstr1CsvWriter`):
  - `public static string ToCsv(Gstr1ExportDto export)`
  - Emits leading `Gstin` column (`Gstin,InvoiceNumber,Date,CustomerName,CustomerGstin,Hsn,Category,TaxableValue,Cgst,Sgst,TotalValue`), one row per invoice sorted by GSTIN then invoice number, plus a `TOTAL` row per GSTIN group.
- Handler (`StoreOS.Application.Reporting.Gstr1Export.GetGstr1ExportHandler`):
  - Implements `IRequestHandler<GetGstr1ExportQuery, Gstr1ExportDto>` using `IReportingDbContext` and `IAccessScope`.
  - Date bounds in Asia/Kolkata tenant timezone converted to UTC for database queries.
  - Explicit Three-Way Exclusion: filters active tax invoices within `[From, To]` date range, explicitly excluding Estimates, Voided, and Refunded sales.
  - Selling Branch GSTIN Grouping: groups rows by the SELLING branch's GSTIN (`branches.gstin`, Plan Section 2 Decision 7), NOT customer GSTIN. Multiple branches sharing the same GSTIN are merged into a single group.
  - Calculates exact group subtotals: `TotalTaxable`, `TotalCgst`, `TotalSgst`, `GrandTotal`.
- API Route (`StoreOS.Api.Controllers.ExportsController`):
  - `GET /api/exports/gstr1?from=&to=&format=csv|json` -> `ExportGstr1`
  - Default CSV output with UTF-8 BOM preamble and `Content-Disposition: attachment; filename="gstr1-<from>-<to>.csv"`, or JSON DTO when requested via `format=json` or `Accept: application/json`.
- Integration Tests (`StoreOS.IntegrationTests.Reporting.Gstr1ExportTests`):
  - (a) Sales across two branches with different GSTINs produce distinct groups with accurate subtotals.
  - (b) Two branches sharing the same GSTIN are combined into one group.
  - (c) Estimates, Voided sales, and Refunded sales within the date range are excluded.
  - (d) Sales outside the `[From, To]` date range are excluded.
  - (e) Each group's subtotals exactly match the sum of its own invoice rows.
  - (f) CSV format export verified for UTF-8 BOM, headers, invoice rows, and TOTAL rows.

## P-806
### QuestPDF Invoice/Estimate Template (DTO-only)
- Configuration (`StoreOS.Api.Program`):
  - `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;`
  - `builder.Services.AddSingleton<StoreOS.Infrastructure.Printing.InvoicePdfRenderer>();`
- Printing DTOs (`StoreOS.Infrastructure.Printing`):
  - `InvoicePdfLineDto(string ItemName, string? Hsn, int Qty, decimal PricePerUnit, decimal Taxable, decimal Cgst, decimal Sgst, string? Imei, bool IsGift)`
  - `InvoicePdfDto(string ShopName, string? ShopAddress, string? ShopPhone, string? ShopEmail, string? ShopGstin, string InvoiceNumber, string InvoiceType, DateTimeOffset SaleDate, string CustomerName, string? CustomerGstin, IReadOnlyList<InvoicePdfLineDto> Lines, decimal TotalTaxable, decimal TotalCgst, decimal TotalSgst, decimal Discount, decimal GrandTotal, decimal AmountPaid, decimal Balance, string? PaymentMode, string? FinanceProvider, string? FinancerName, string? Description, string? IrnDisplay, string? IrnQrDataUrl)`
- Renderer (`StoreOS.Infrastructure.Printing.InvoicePdfRenderer`):
  - `public byte[] Render(InvoicePdfDto dto)`
  - Pure DTO-to-bytes rendering with zero database awareness.
  - A4 format with structured Header, Line Items Table (with Gift and IMEI annotations), and Summary/Payment Footer.
  - Reserved slots for `IrnDisplay` and `IrnQrDataUrl` (Phase 8 e-invoicing roadmap).
- DTO Purity Rule:
  - Every PDF renderer in StoreOS accepts only flat, print-ready DTOs and never touches raw domain entities, DbContexts, or external services within template generation.
- Integration Tests (`StoreOS.IntegrationTests.Printing.InvoicePdfRendererTests`):
  - (a) Rendering well-formed `InvoicePdfDto` produces valid PDF byte array starting with `%PDF` magic bytes.
  - (b) Rendering succeeds when every optional field is null (minimal cash walk-in sale).
  - (c) Rendering with Gift and IMEI line items produces valid PDF with appropriate size expansion.

## P-807
### QuestPDF Repair Ticket/Invoice & Supplier Return Note Templates
- Printing DTOs (`StoreOS.Infrastructure.Printing`):
  - `RepairPartLineDto(string PartName, int Quantity, decimal ChargeAmount)`
  - `RepairPdfDto(string ShopName, string? ShopAddress, string? ShopPhone, string JobNumber, DateTimeOffset ReceivedAt, string CustomerName, string? CustomerPhone, string? DeviceBrand, string? DeviceModel, string? SerialNumber, string? Imei1, string? ProblemDescription, string Status, decimal? EstimatedCost, decimal? FinalTotal, decimal? AdvancePaid, decimal? BalanceDue, DateOnly? WarrantyUntil, IReadOnlyList<RepairPartLineDto> Parts, string? InvoiceNumber)`
  - `SupplierReturnLineDto(string ItemName, int Qty, decimal ReturnRate, decimal LineTotal, string[]? ImeiNumbers)`
  - `SupplierReturnPdfDto(string ShopName, string ReturnNumber, DateOnly ReturnDate, string SupplierName, string? SupplierPhone, string? SupplierAddress, IReadOnlyList<SupplierReturnLineDto> Lines, decimal TotalAmount, string SettlementType, string? Reason)`
- Renderers (`StoreOS.Infrastructure.Printing`):
  - `RepairPdfRenderer`: `public byte[] Render(RepairPdfDto dto)` (Header with shop info + job number + status + customer, Content with device details and parts table, Footer with commercials, warranty, and linked invoice number). Registered as singleton in `Program.cs`.
  - `SupplierReturnPdfRenderer`: `public byte[] Render(SupplierReturnPdfDto dto)` (Header with shop + return number + date + supplier info, Content with lines table including IMEIs, Footer with total amount, settlement type, and reason). Registered as singleton in `Program.cs`.
- DTO Purity Rule:
  - Both renderers take only flat, print-ready DTOs without any entity or database awareness.
- Integration Tests (`StoreOS.IntegrationTests.Printing.RepairAndSupplierReturnPdfTests`):
  - (a) `RepairPdfRenderer` renders successfully with full data (device, parts table, advance, balance, warranty, invoice reference).
  - (b) `RepairPdfRenderer` renders successfully when all optional fields are null (newly received job with no commercials yet).
  - (c) `SupplierReturnPdfRenderer` renders successfully with multiple lines including IMEI-bearing lines.

## P-808
### QuestPDF Daily Sales and Inventory Report Templates
- Configuration (`StoreOS.Api.Program`):
  - `builder.Services.AddSingleton<StoreOS.Infrastructure.Printing.ReportPdfRenderer>();`
- Renderer (`StoreOS.Infrastructure.Printing.ReportPdfRenderer`):
  - `public byte[] RenderDailySales(DailySalesReportDto dto, string? shopName = null)`: Reuses existing `StoreOS.Application.Reporting.DailySales.DailySalesReportDto` directly without duplicate DTOs. Renders Header (shop branding, date), Sales & Commercial Summary, Inventory Valuation Snapshot, Payment Breakdown Table, Category Sales Table, and Itemized Sales Log (with IMEI annotations).
  - `public byte[] RenderInventoryExport(InventoryExportDto dto, string? shopName = null)`: Reuses existing `StoreOS.Application.Reporting.InventoryExport.InventoryExportDto` directly. Renders Header, Physical Inventory Items Table (with IMEI summaries), and Repair & Service Catalog Table.
- DTO Purity & Reuse:
  - Follows strict DTO purity, accepting flat application DTOs and generating PDF bytes without database access.
- Integration Tests (`StoreOS.IntegrationTests.Printing.ReportPdfRendererTests`):
  - (a) `RenderDailySales` with full seeded data produces valid PDF starting with `%PDF` magic bytes.
  - (b) `RenderInventoryExport` with physical and service items produces valid PDF.
  - (c) Rendering with empty itemized logs / empty inventory lists succeeds gracefully without throwing.

## P-809
### Primary Document PDF Endpoints & Shop Branding Fallback
- Queries and DTOs (`StoreOS.Application`):
  - `StoreOS.Application.Sales.GetSaleById.SaleDetailDto`: Added `BranchId` to provide selling branch context for branding lookup.
  - `GetSupplierReturnByIdQuery(Guid Id) : IRequest<SupplierReturnDto>, IReadOnlyRequest` (`StoreOS.Application.Suppliers.GetReturnById`)
  - `SupplierReturnDto(Guid Id, Guid TenantId, Guid BranchId, string ReturnNumber, DateOnly ReturnDate, Guid SupplierId, string SupplierName, string? SupplierPhone, string? SupplierAddress, int TotalQty, decimal TotalAmount, string SettlementType, string? Reason, string? Notes, IReadOnlyList<SupplierReturnItemDto> Items)`
  - `SupplierReturnItemDto(Guid Id, string ItemName, int Qty, decimal ReturnRate, decimal LineTotal, string[]? ImeiNumbers)`
- Shop Branding Helper (`StoreOS.Api.Controllers.ShopBrandingHelper`):
  - `public static async Task<ResolvedShopBranding> ResolveAsync(Guid branchId, IAppDbContext db, CancellationToken ct)`
  - Resolves `shopName`, `address`, `phone`, `email`, and `gstin` from the branch-level `"shop_profile"` setting JSON.
  - Graceful Fallback: if the `"shop_profile"` setting is absent, empty, or unparseable, falls back seamlessly to `branches.name`/`address`/`phone`/`gstin` without throwing.
- API Routes & Mapping Isolation:
  - `GET /api/sales/{id}/invoice.pdf` (`SalesController`): Maps `SaleDetailDto` -> `InvoicePdfDto` in controller and renders via `InvoicePdfRenderer`.
  - `GET /api/repair-jobs/{id}/ticket.pdf` (`RepairJobsController`): Maps `RepairJobDto` + parts -> `RepairPdfDto` in controller and renders via `RepairPdfRenderer`.
  - `GET /api/supplier-returns/{id}/note.pdf` (`SupplierReturnsController`): Maps `SupplierReturnDto` -> `SupplierReturnPdfDto` in controller and renders via `SupplierReturnPdfRenderer`.
- DTO Purity Rule:
  - All entity-to-DTO mapping logic lives strictly in each controller, never inside the renderer classes, preserving pure DTO-to-bytes rendering.
- Integration Tests (`StoreOS.IntegrationTests.Printing.PdfEndpointsTests`):
  - (a) All 3 endpoints return 200 with `Content-Type: application/pdf` and valid `%PDF` bytes (>100 bytes) for fully-populated records.
  - (b) Invoice PDF endpoint succeeds when no `"shop_profile"` setting exists, falling back to branch master data.
  - (c) Nonexistent ID on any of the three endpoints returns 404 (reusing existing not-found handling).

## P-901
### JSON Backup Export (tenant/export) & Object Storage
- Storage Abstractions & Service (`StoreOS.Application.Abstractions` / `StoreOS.Infrastructure.Platform`):
  - `IObjectStorageService`:
    - `Task<string> UploadAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct)`
    - `Task<StoredObject?> GetAsync(string bucket, string key, CancellationToken ct)`
    - `Task EnsureBucketsAsync(CancellationToken ct = default)`
  - `StoredObject(byte[] Content, string ContentType)`
  - `MinioObjectStorageService`: Implements `IObjectStorageService` using `IAmazonS3` client pointed at MinIO dev container (`http://localhost:9000`, `storeos_minio` credentials, `ForcePathStyle = true`).
  - `ObjectStorageBucketInitializer`: `IHostedService` ensuring `storeos-media` and `storeos-backups` buckets exist on startup.
  - MinIO Bucket convention: `"storeos-backups"`, Key convention: `"{tenantId}/backup-{timestamp}.json"`.
- Backup Command & DTOs (`StoreOS.Application.Platform.BackupExport`):
  - `ExportTenantDataCommand() : IRequest<TenantBackupDto>, IRequiresPermission, IHighImpactOperation`: Requires permission `"settings:manage"`.
  - `TenantBackupDto(string SchemaVersion, DateTimeOffset ExportedAt, Guid TenantId, object Tenant, object[] Branches, object[] Settings, object[] Users, object[] Roles, object[] RolePermissions, object[] UserBranches, object[] UserRoleAssignments, object[] Products, object[] BranchInventories, object[] Purchases, object[] InventoryImeis, object[] InventoryMovements, object[] Sales, object[] SaleItems, object[] CustomerPayments, object[] Customers, object[] CustomerLedgerEntries, object[] Suppliers, object[] SupplierTransactions, object[] SupplierLedgerEntries, object[] SupplierReturns, object[] SupplierReturnItems, object[] Expenses, object[] Liabilities, object[] LiabilityPayments, object[] RepairJobs, object[] RepairJobParts, object[] RepairJobStatusHistory)`: SchemaVersion is `"1.0"`.
  - Flat table DTOs: `BackupTenantDto`, `BackupBranchDto`, `BackupSettingDto`, `BackupUserDto` (exposing Argon2 `password_hash` and `passwordHash` alias), `BackupRoleDto`, `BackupRolePermissionDto`, `BackupUserBranchDto`, `BackupUserRoleAssignmentDto`, `BackupProductDto`, `BackupBranchInventoryDto`, `BackupPurchaseDto`, `BackupInventoryImeiDto`, `BackupInventoryMovementDto`, `BackupSaleDto`, `BackupSaleItemDto`, `BackupCustomerPaymentDto`, `BackupCustomerDto`, `BackupCustomerLedgerEntryDto`, `BackupSupplierDto`, `BackupSupplierTransactionDto`, `BackupSupplierLedgerEntryDto`, `BackupSupplierReturnDto`, `BackupSupplierReturnItemDto`, `BackupExpenseDto`, `BackupLiabilityDto`, `BackupLiabilityPaymentDto`, `BackupRepairJobDto`, `BackupRepairJobPartDto`, `BackupRepairJobStatusHistoryDto`.
- Sanctioned `IgnoreQueryFilters()` Exception:
  - `ExportTenantDataHandler` executes `SET LOCAL app.all_branches = 'true'` inside the transaction and uses `IgnoreQueryFilters()` with explicit `tenant_id` scoping to export all branches' data regardless of the exporting user's branch scope. Documented as a sanctioned, narrow exception alongside P-303, P-507, P-606, P-708.
- API Route (`StoreOS.Api.Controllers.PlatformController`):
  - `POST /api/tenant/export`: Invokes `ExportTenantDataCommand` and returns `ActionResult<TenantBackupDto>`.
- Integration Tests (`StoreOS.IntegrationTests.Platform.ExportTenantDataTests`):
  - (a) Multi-branch export with single-branch scoped user successfully exports all branches' data.
  - (b) `SchemaVersion` is present and `"1.0"`.
  - (c) `password_hash` values are present and Argon2-formatted strings.
  - (d) Serialized JSON is uploaded to MinIO bucket and matches returned DTO.
  - (e) Caller without `settings:manage` returns 403.
  - (f) `MinioObjectStorageService` round trip test (upload, get, missing returns null).

## P-902
### JSON Restore/Import (tenant/import)
- Import Command & Result DTO (`StoreOS.Application.Platform.BackupImport`):
  - `ImportTenantDataCommand(TenantBackupDto Backup) : IRequest<ImportResultDto>, IRequiresPermission, IHighImpactOperation, IIdempotentRequest`:
    - `RequiredPermission => "settings:manage"`
    - `IdempotencyScope => "tenant.import"`
    - `IdempotencyRequired => true`
  - `ImportResultDto(bool Success, Dictionary<string, int> RecordCounts, string? ErrorMessage)`
- API Route (`StoreOS.Api.Controllers.PlatformController`):
  - `POST /api/tenant/import`: Accepts `TenantBackupDto`, enforces mandatory `Idempotency-Key` header, returns `200 OK` on success or `400 Bad Request` on validation failure.
- Transactional Staging & Restore Mechanism:
  - Runs inside the ambient transaction from `TransactionBehavior` (Invariant 12: all or nothing).
  - Sets `SET LOCAL app.all_branches = 'true'` to allow cross-branch replacement while preserving strict `app.tenant_id` RLS scoping.
  - Reverse-dependency deletion: Uses `delete_tenant_data_for_restore(uuid)` (a `SECURITY DEFINER` function in PostgreSQL) which safely purges existing tenant rows across all ~28 child tables while honoring the append-only invariant on ledger tables.
  - Forward-dependency insertion: Deserializes backup DTO arrays and rehydrates entities via factory variants, inserting in order: `branches`, `settings`, `roles`, `role_permissions`, `users`, `user_branches`, `user_role_assignments`, `products`, `branch_inventory`, `purchases`, `inventory_imeis`, `inventory_movements`, `customers`, `sales`, `sale_items`, `customer_payments`, `customer_ledger_entries`, `suppliers`, `supplier_transactions`, `supplier_ledger_entries`, `supplier_returns`, `supplier_return_items`, `expenses`, `liabilities`, `liability_payments`, `repair_jobs`, `repair_job_parts`, `repair_job_status_history`.
- Entity `Rehydrate(...)` Variants:
  - Added `Rehydrate(...)` static factory variants to all domain entities whose standard `Create` method generates new GUIDs or validates "new business object" rules that would reject historical backup records: `Tenant`, `Branch`, `User`, `Role`, `Product`, `BranchInventory`, `Purchase`, `InventoryImei`, `InventoryMovement`, `Sale`, `SaleItem`, `CustomerPayment`, `Customer`, `CustomerLedgerEntry`, `Supplier`, `SupplierTransaction`, `SupplierLedgerEntry`, `SupplierReturn`, `SupplierReturnItem`, `Expense`, `Liability`, `LiabilityPayment`, `RepairJob`, `RepairJobPart`, `RepairJobStatusHistory`.
- Database Migration:
  - Migration: `20261007201557_030_TenantRestoreFunction` (`dotnet ef migrations add 030_TenantRestoreFunction --context StoreOsDbContext`) adds the `public.delete_tenant_data_for_restore(p_tenant_id uuid)` function and grants execute to `storeos_app`.
- Integration Tests (`StoreOS.IntegrationTests.Platform.ImportTenantDataTests`):
  - (a) Full round-trip: export seeded tenant data, import into same tenant, and verify all table row counts match pre-export state exactly.
  - (b) Unrecognized `SchemaVersion` fails cleanly with `Success: false` and leaves database completely untouched.
  - (c) Mid-import failure (simulated FK violation) rolls back entire transaction, leaving pre-import data fully intact (Invariant 12).
  - (d) Caller without `settings:manage` returns 403.

## P-903
### Desktop Migration Tool (pz-migrate): Schema Detect + Dry-Run + Reconciliation Report
- Console Application (`tools/pz-migrate`):
  - Project: `tools/pz-migrate/pz-migrate.csproj` (`net10.0`, references `StoreOS.Domain`, `StoreOS.Application`, `StoreOS.Infrastructure`).
  - Test Project: `tools/pz-migrate.Tests/pz-migrate.Tests.csproj` (`net10.0`, xUnit + FluentAssertions).
- Desktop Schema Models (`PzMigrate.DesktopBackupSchema`):
  - POCOs matching SQLite desktop backup format: `DesktopBackup`, `DesktopSetting`, `DesktopItem`, `DesktopPurchase`, `DesktopSale`, `DesktopSaleItem`, `DesktopCustomer`, `DesktopCustomerPayment`, `DesktopExpense`, `DesktopSupplier`, `DesktopSupplierTransaction`, `DesktopLiability`, `DesktopLiabilityPayment`, `DesktopInventoryImei`, `DesktopSupplierReturn`, `DesktopSupplierReturnItem`, `DesktopRepairService`, `DesktopRepairJob`, `DesktopRepairJobPart`, `DesktopRepairJobStatusHistory`.
- Reconciliation Report (`PzMigrate`):
  - `ReconciliationReport(Dictionary<string, int> RecordCountsByTable, decimal SumSaleGrandTotals, decimal SumPayments, int SumStockQty, decimal SumActiveCreditBalances)`:
    - `SumSaleGrandTotals`: Sum of `Sales.GrandTotal`.
    - `SumPayments`: Sum of `CustomerPayments.Amount`.
    - `SumStockQty`: Sum of `Items.StockQty`.
    - `SumActiveCreditBalances`: Sum of `Customers.CreditBalance` where `CreditBalance > 0`.
    - `RecordCountsByTable`: Counts for all 19 desktop tables.
- Migration Service (`PzMigrate.DesktopMigrationService`):
  - `DetectSchemaOrThrow(string json)`: Validates that input is a valid JSON object containing desktop backup top-level keys (`items`, `sales`, `customers`) and rejects cloud backup shapes (`schemaVersion`) or malformed files with clear errors.
  - `RunDryRun(string backupPath)`: Parses desktop JSON file, detects schema, computes `ReconciliationReport`, writes `reconciliation-report.json` next to the backup, and outputs zero database connections.
- CLI Syntax (`tools/pz-migrate/Program.cs`):
  - `pz-migrate import --backup <path> --tenant-name <name> [--dry-run]`
  - `pz-migrate reconcile-customers --tenant <id> [--dry-run]`
- Unit Tests (`tools/pz-migrate.Tests/DryRunImportTests.cs`):
  - (a) Well-formed realistic desktop backup produces exact hand-computed reconciliation sums and creates `reconciliation-report.json`.
  - (b) Unrecognized/malformed JSON files are rejected with clear error messages without creating any report file.
  - (c) Dry-run operates purely on the input file with zero database connections.
  - (d) CLI invocation via `Program.Main` succeeds with exit code 0.

## P-904
### pz-migrate Record-Level Dedupe & Reconcile Customers CLI
- Database Schema Updates & Migration:
  - `Sale`: Added `public string? OriginalInvoiceNumber { get; private set; }` and `public void SetOriginalInvoiceNumber(string? originalInvoiceNumber)`. Mapped in `SaleConfiguration` to column `original_invoice_number` (`text`, nullable).
  - `Customer`: Added `public string? DesktopId { get; private set; }` and `public void SetDesktopId(string? desktopId)`. Mapped in `CustomerConfiguration` to column `desktop_id` (`text`, nullable) with filtered index `ix_customers_tenant_desktop_id` (`WHERE desktop_id IS NOT NULL`).
  - Migration: `20261007202708_031_MigrationSupportColumns` (`dotnet ef migrations add 031_MigrationSupportColumns --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api --context StoreOsDbContext`).
- Committing Import Pipeline (`PzMigrate.DesktopMigrationService.RunImportAsync`):
  - 1. Tenant & Branch: Looks up or creates `Tenant` by name and default `Branch` (code `"B01"`, name `"Main Branch"`).
  - 2. In-memory ID remapping: Maintains dictionaries (`itemMap`, `customerMap`, `supplierMap`, `purchaseMap`, `saleMap`) remapping legacy integer/string IDs to cloud GUIDs and matching against pre-existing rows on successive runs.
  - 3. Natural-Key Record Deduplication:
    - Sales: `(tenant_id, original_invoice_number)` — skips duplicate if original invoice exists.
    - IMEIs: `(tenant_id, imei)` — skips duplicate if IMEI exists in tenant.
    - Purchases: `(tenant_id, supplier_invoice_no, supplier_id, purchase_date)` — skips duplicate if matching purchase exists.
    - Customers (Two-Tier): Primary match on `(tenant_id, desktop_id)`. If phone differs and collides with another customer, records conflict in `migration-conflicts.json` and skips. If phone differs and does not collide, updates customer's phone via `UpdateProfile(...)`. Secondary fallback match on `(tenant_id, phone)` stamps `desktop_id` and skips. Otherwise, inserts new customer.
  - 4. Products & Inventory: Inserts products (with category normalization), initial `BranchInventory` with desktop stock quantity.
  - 5. Conflict Logging: Writes `migration-conflicts.json` next to backup on phone collisions without aborting the batch.
- Customer Reconciliation & Merging:
  - `DesktopMigrationService.MergeCustomersAsync(StoreOsDbContext db, Guid survivingCustomerId, Guid duplicateCustomerId)`: Re-points `sales.customer_id` and `customer_ledger_entries.customer_id` from duplicate customer to surviving customer, deletes duplicate customer, and clears change tracker.
  - CLI command: `pz-migrate reconcile-customers --tenant <id> [--dry-run] [--surviving <id> --duplicate <id>]` lists conflicting phone groups and allows merging.
- Integration Tests (`tools/pz-migrate.Tests/CommittingImportTests.cs`):
  - (a) First committing import creates expected entities and record counts matching P-903 report.
  - (b) Re-running import on unchanged backup creates zero additional rows (full idempotency).
  - (c) Re-running on updated backup imports only new sales and updates existing customer's phone without creating duplicates.
  - (d) Phone collision edge case logs to `migration-conflicts.json` and skips without batch abortion.
  - (e) Customer merge re-points sales and ledger entries and deletes duplicate.
  - (f) CLI commands execution passes.

## P-905
### Nightly Reconciliation Hangfire Job (5 Sweeps)
- Reconciliation Job (`StoreOS.Infrastructure.Jobs.ReconciliationJob`):
  - `Task RunAsync(CancellationToken ct)`: Enumerate active tenants, runs sweeps inside `ITenantJobRunner.RunAsync` with per-tenant transaction and error isolation.
  - 5 Consistency Sweeps:
    - `Task ReconcileStockAsync(Guid tenantId, Guid branchId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)` (Sweep 1): Compares `branch_inventory.stock_qty` with `SUM(inventory_movements.qty_delta)` per product/branch.
    - `Task ReconcileSalePaymentsAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)` (Sweep 2): Compares `sales.amount_paid` with `SUM(customer_payments.amount)` per sale.
    - `Task ReconcileCustomerRevenueAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)` (Sweep 3): Compares `customers.total_purchases` with `SUM(sales.grand_total WHERE status='Active')` per customer.
    - `Task ReconcileCustomerCreditAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)` (Sweep 4): Compares derived unpaid sales balance (`SUM(sales.grand_total - sales.amount_paid)`) with customer ledger net balance (`SUM(customer_ledger_entries.debit - credit)`).
    - `Task ReconcileSupplierPayablesAsync(Guid tenantId, StoreOsDbContext db, IAuditWriter audit, CancellationToken ct)` (Sweep 5): Compares derived net purchase liability (`SUM(purchases.qty * purchases.purchase_rate) - SUM(supplier_transactions.amount WHERE type='Payment')`) with supplier ledger net payable (`SUM(supplier_ledger_entries.credit - debit)`).
  - Discrepancy Alerting: Logs high-severity audit entries via `IAuditWriter.WriteAsync` with action `"reconciliation.drift_detected"` containing entity type, ID, expected, and actual values.
  - Metrics: Emits OpenTelemetry counter `reconciliation_drift_total` via Meter `"StoreOS.Reconciliation"`.
- Idempotency Key Purge Job (`StoreOS.Infrastructure.Jobs.IdempotencyKeyPurgeJob`):
  - `Task RunAsync(CancellationToken ct)`: Deletes `idempotency_keys` older than 7 days per tenant using `ITenantJobRunner`.
- Hangfire Setup (`StoreOS.Api.Program`):
  - Registered `AddHangfire` with PostgreSQL storage and `AddHangfireServer`.
  - Registered recurring jobs: `"nightly-reconciliation"` (Cron.Daily) and `"idempotency-key-purge"` (Cron.Daily).
- Integration Tests (`StoreOS.IntegrationTests.Platform.ReconciliationJobTests`):
  - (a) Consistent dataset produces zero drift entries across all 5 sweeps.
  - (b) Deliberately seeded drift across each of the 5 sweeps detects and logs all 5 independently with exact entity IDs and expected/actual values.
  - (c) Iterates all active tenants and their branches independently.
  - (d) Failure in one tenant does not stop or abort processing for remaining tenants.

## P-906
### Hangfire Wiring + Correlation-Id Propagation into Background Jobs
- Centralized Hangfire Setup (`StoreOS.Api.Jobs.HangfireSetup`):
  - `public static IServiceCollection AddHangfireConfiguration(this IServiceCollection services, IConfiguration configuration)`: Registers PostgreSQL storage, Hangfire server, and background jobs (`ReconciliationJob`, `IdempotencyKeyPurgeJob`).
  - `public static IApplicationBuilder UseHangfireConfiguration(this IApplicationBuilder app)`: Schedules recurring jobs with fresh correlation IDs on scheduled runs:
    - `"nightly-reconciliation"`: `job => job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None)` (Cron.Daily).
    - `"idempotency-key-purge"`: `job => job.RunAsync(CancellationToken.None)` (Cron.Daily).
- Explicit-Parameter Correlation-Id Convention for Background Jobs:
  - Rejects fragile argument scanning in favor of an explicit named parameter: every background job method that carries a correlation id accepts `string correlationId` as an explicit parameter.
  - `ReconciliationJob.RunAsync(string correlationId, CancellationToken ct)`:
    - Encloses execution in `using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))`.
    - Sets `correlationAccessor.CorrelationId = correlationId` inside `ITenantJobRunner.RunAsync` scope so all audit entries created by `IAuditWriter` carry the exact correlation ID.
- On-Demand Admin Trigger Route (`StoreOS.Api.Controllers.PlatformController`):
  - `POST /api/platform/reconciliation/run-now`:
    - Authorizes caller via `TriggerReconciliationCommand` (`IHighImpactOperation`, `settings:manage`).
    - Inherits triggering HTTP request's correlation ID from `ICorrelationContextAccessor`.
    - Enqueues job via `IBackgroundJobClient.Enqueue<ReconciliationJob>(job => job.RunAsync(correlation.CorrelationId, CancellationToken.None))`.
    - Returns `Ok(new { jobId, correlationId = correlation.CorrelationId })`.
- Integration Tests (`StoreOS.IntegrationTests.Platform.ReconciliationCorrelationTests`):
  - (a) Triggering `run-now` with known `X-Correlation-ID` propagates that exact correlation ID into resulting audit log entries.
  - (b) Scheduled runs generate their own distinct fresh correlation IDs.
  - (c) Caller without `settings:manage` returns 403.
