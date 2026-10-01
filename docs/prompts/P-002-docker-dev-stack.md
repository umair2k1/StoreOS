# P-002 — Docker dev stack: postgres, minio, otel-collector

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-002** of the StoreOS Cloud build ("Docker dev stack: postgres, minio, otel-collector") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-001 is complete and committed: `store-os-cloud/` exists with the full .NET
  solution skeleton building green, plus `docs/BUILD_LOG.md` and `docs/ARTIFACTS.md`.
- `deploy/` currently contains only a `.gitkeep`.
- Do not modify anything under `backend/` in this prompt.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** **App role for RLS (DECISIONS D-03).** Add to this prompt (paths relative to `deploy/`):

`deploy/db/init/01-app-role.sh` (make it executable, LF line endings; runs once when the data volume is first created):
```sh
#!/bin/sh
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" <<-EOSQL
  DO \$\$ BEGIN
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'storeos_app') THEN
      CREATE ROLE storeos_app LOGIN PASSWORD '${STOREOS_APP_PASSWORD}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;
    END IF;
  END \$\$;
  GRANT CONNECT ON DATABASE ${POSTGRES_DB} TO storeos_app;
  GRANT USAGE ON SCHEMA public TO storeos_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE ${POSTGRES_USER} IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO storeos_app;
  ALTER DEFAULT PRIVILEGES FOR ROLE ${POSTGRES_USER} IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO storeos_app;
EOSQL
```
`deploy/db/post-migrate.sql` (idempotent; re-run after EVERY migration in dev, CI, staging and production; it enforces the "append-only tables have no UPDATE/DELETE grant" rule of Plan §6.5):
```sql
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO storeos_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO storeos_app;
DO $$
DECLARE t text;
BEGIN
  FOREACH t IN ARRAY ARRAY['audit_log','customer_ledger_entries','supplier_ledger_entries',
      'inventory_movements','repair_job_status_history','customer_payments','liability_payments'] LOOP
    IF to_regclass('public.' || t) IS NOT NULL THEN
      EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON public.%I FROM storeos_app', t);
    END IF;
  END LOOP;
  IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
    REVOKE ALL ON public."__EFMigrationsHistory" FROM storeos_app;
  END IF;
END $$;
```
`deploy/db/apply-post-migrate.sh`: runs `psql "$STOREOS_MIGRATIONS_CONNECTION_URI" -v ON_ERROR_STOP=1 -f deploy/db/post-migrate.sql` (the URI form, e.g. `postgresql://storeos:storeos_dev_password@localhost:5432/storeos`). Update the verification of this prompt to prove: connecting as `storeos_app`, `SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user` returns `f | f`.

Create `deploy/docker-compose.yml` with exactly these four services (dev
only — API container comes in P-003, this compose file references it by build context so it
will work once P-003 exists, but for THIS prompt you only need postgres, minio, and
otel-collector to start; add an `api` service stub referencing `../deploy/Dockerfile.api`
with `profiles: ["full"]` so `docker-compose up -d` without the `full` profile does not try
to build it before P-003 exists):

```yaml
version: "3.9"
services:
  postgres:
    image: postgres:16-alpine
    container_name: storeos-postgres
    restart: unless-stopped
    environment:
      POSTGRES_USER: storeos
      POSTGRES_PASSWORD: storeos_dev_password
      POSTGRES_DB: storeos
      STOREOS_APP_PASSWORD: storeos_app_dev_password
    ports:
      - "5432:5432"
    volumes:
      - storeos_pg_data:/var/lib/postgresql/data
      - ./db/init:/docker-entrypoint-initdb.d:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U storeos -d storeos"]
      interval: 5s
      timeout: 5s
      retries: 10

  minio:
    image: minio/minio:latest
    container_name: storeos-minio
    restart: unless-stopped
    command: server /data --console-address ":9001"
    environment:
      MINIO_ROOT_USER: storeos_minio
      MINIO_ROOT_PASSWORD: storeos_minio_password
    ports:
      - "9000:9000"
      - "9001:9001"
    volumes:
      - storeos_minio_data:/data
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:9000/minio/health/live"]
      interval: 5s
      timeout: 5s
      retries: 10

  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.96.0
    container_name: storeos-otel-collector
    restart: unless-stopped
    command: ["--config=/etc/otel-collector-config.yaml"]
    volumes:
      - ./otel-collector-config.yaml:/etc/otel-collector-config.yaml
    ports:
      - "4317:4317"
      - "4318:4318"

  api:
    build:
      context: ../backend
      dockerfile: ../deploy/Dockerfile.api
    container_name: storeos-api
    profiles: ["full"]
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    environment:
      ConnectionStrings__Default: "Host=postgres;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password"
      ConnectionStrings__Reporting: "Host=postgres;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password"
    ports:
      - "5000:8080"

volumes:
  storeos_pg_data:
  storeos_minio_data:
```

Create `deploy/otel-collector-config.yaml`:

```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

exporters:
  debug:
    verbosity: detailed

processors:
  batch:

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [batch]
      exporters: [debug]
    metrics:
      receivers: [otlp]
      processors: [batch]
      exporters: [debug]
    logs:
      receivers: [otlp]
      processors: [batch]
      exporters: [debug]
```

## 4. TASK

1. Create `deploy/docker-compose.yml` and `deploy/otel-collector-config.yaml` exactly
   as given in the CONTEXT BLOCK.
2. Start the non-`full`-profile services: `docker-compose -f deploy/docker-compose.yml up -d
   postgres minio otel-collector`.
3. Wait for `postgres` and `minio` health checks to report healthy (poll
   `docker-compose -f deploy/docker-compose.yml ps` every 2 seconds, up to 60 seconds).
4. Confirm Postgres is reachable: `docker exec storeos-postgres pg_isready -U storeos -d
   storeos` must print `accepting connections`.
5. Confirm MinIO console responds: `curl -sf http://localhost:9000/minio/health/live` must
   return HTTP 200 (empty body is fine).
6. Leave the stack running for the verification step below.

## 5. HARD CONSTRAINTS

- Do not change exposed port numbers from those listed (5432, 9000,
  9001, 4317, 4318, 5000) — later prompts' connection strings depend on these exact values.
- Do not add a pgAdmin or other extra service in this prompt; keep it to exactly the four
  services listed.
- The `api` service must stay behind `profiles: ["full"]` until P-003 creates
  `deploy/Dockerfile.api` — otherwise `docker-compose up -d` (without specifying services)
  will fail trying to build a nonexistent Dockerfile.

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
docker-compose -f deploy/docker-compose.yml up -d postgres minio otel-collector
sleep 15
docker-compose -f deploy/docker-compose.yml ps
# Expected: postgres and minio show "healthy"; otel-collector shows "Up".
docker exec storeos-postgres pg_isready -U storeos -d storeos
# Expected: "/var/run/postgresql:5432 - accepting connections"
curl -sf -o /dev/null -w "%{http_code}" http://localhost:9000/minio/health/live
# Expected: 200
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-002 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-002 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-002` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact service names, ports, and credentials (Postgres user/password/db
name, MinIO root user/password) so later prompts (P-101 connection string, P-901 backup
storage) can reference them verbatim.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-002: <one-line summary>"` only after verification is
   fully green.
