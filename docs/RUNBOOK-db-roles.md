# Runbook: Database Roles & Row Level Security (RLS) Isolation

## 1. Role Architecture (DECISIONS D-03)

StoreOS uses two dedicated database roles to ensure tenant isolation through PostgreSQL Row Level Security (RLS). PostgreSQL never applies RLS policies to superusers or roles with `BYPASSRLS`, even when `FORCE ROW LEVEL SECURITY` is set. Therefore, application connections must never use a superuser account.

| Role | Attributes | Intended Use | Connection Source |
|---|---|---|---|
| `storeos` (dev) / `storeos_owner` (prod/test) | Superuser / Owner | Schema migrations (`dotnet ef`), database updates, seeding, provisioning, restore drills. | `STOREOS_MIGRATIONS_CONNECTION` env var / `StoreOsDbContextFactory` |
| `storeos_app` | `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` | API runtime, background worker jobs, application queries. | `ConnectionStrings:Default`, `ConnectionStrings:Reporting` in `appsettings.json` |

## 2. Permissions and Grants

### Owner Role (`storeos` / `storeos_owner`)
- Owns all database schemas and tables.
- Runs DDL operations and manages extensions.
- Bypasses RLS to execute migrations.

### Application Role (`storeos_app`)
- Granted `CONNECT ON DATABASE` and `USAGE ON SCHEMA public`.
- Granted DML permissions (`SELECT`, `INSERT`, `UPDATE`, `DELETE`) on standard tables.
- Granted `USAGE, SELECT` on all sequences.
- **Append-Only Restrictions:** `UPDATE`, `DELETE`, and `TRUNCATE` are revoked on immutable tables:
  - `audit_log`
  - `customer_ledger_entries`
  - `supplier_ledger_entries`
  - `inventory_movements`
  - `repair_job_status_history`
  - `customer_payments`
  - `liability_payments`
- Access to `__EFMigrationsHistory` is revoked.

## 3. Post-Migration Script (`deploy/db/post-migrate.sql`)

Because EF Core migrations create new tables with owner-only permissions by default, `deploy/db/post-migrate.sql` must be executed after every migration run.

The post-migrate script:
1. Re-grants DML permissions on all tables in `public` to `storeos_app`.
2. Re-grants usage on all sequences in `public` to `storeos_app`.
3. Revokes `UPDATE`, `DELETE`, and `TRUNCATE` on append-only ledger and audit tables.
4. Revokes all permissions on `__EFMigrationsHistory` from `storeos_app`.

Run post-migrate script locally:
```bash
./deploy/db/apply-post-migrate.sh
# Or manually via docker compose:
docker compose -f deploy/docker-compose.yml exec -T postgres psql -U storeos -d storeos < deploy/db/post-migrate.sql
```

## 4. Local Reset & Reproducing Changes

When `deploy/db/init/01-app-role.sh` or database initialization logic changes, recreate the database from scratch:

```bash
# 1. Stop containers and destroy the persistent postgres volume:
docker compose -f deploy/docker-compose.yml down -v

# 2. Re-start postgres to run initialization scripts:
docker compose -f deploy/docker-compose.yml up -d postgres

# 3. Verify storeos_app role was created:
docker compose -f deploy/docker-compose.yml exec -T postgres psql -U storeos -d storeos -c "\du storeos_app"
```
