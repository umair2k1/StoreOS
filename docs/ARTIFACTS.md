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
