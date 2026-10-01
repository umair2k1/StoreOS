# P-103 — Settings key/value entity/migration

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-103** of the StoreOS Cloud build ("Settings key/value entity/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-102 complete: `tenants` and `branches` tables exist and migrated;
  `Tenant`/`Branch` entities exist in `StoreOS.Domain/Tenancy/`.
- Check `docs/ARTIFACTS.md` for the exact `Tenant`/`Branch` signatures before writing FKs.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.2), using a composite key with `COALESCE` for the
nullable `branch_id` (NULL = tenant-level default):

```sql
CREATE TABLE settings (
  tenant_id uuid NOT NULL,
  branch_id uuid NULL REFERENCES branches(id),
  key text NOT NULL,
  value jsonb NOT NULL,
  PRIMARY KEY (tenant_id, COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid), key)
);
```

EF Core cannot express a `COALESCE`-based composite PK directly via the fluent API in a
portable way, so implement this with a **generated computed column** approach instead:
add a computed column `branch_id_key` that stores `COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid)`,
and make the actual primary key `(tenant_id, branch_id_key, key)`. Use a raw SQL migration
operation for this since EF's fluent API doesn't have first-class computed-column support
across providers as cleanly as raw SQL:

```csharp
// In the migration Up() method, after CreateTable with tenant_id/branch_id/key/value:
migrationBuilder.Sql(@"
    ALTER TABLE settings ADD COLUMN branch_id_key uuid
        GENERATED ALWAYS AS (COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid)) STORED;
    ALTER TABLE settings ADD CONSTRAINT pk_settings PRIMARY KEY (tenant_id, branch_id_key, key);
");
```

Create `backend/src/StoreOS.Domain/Tenancy/Setting.cs`:

```csharp
namespace StoreOS.Domain.Tenancy;

public class Setting
{
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Key { get; private set; } = default!;
    public string ValueJson { get; private set; } = default!;

    private Setting() { }

    public static Setting Create(Guid tenantId, Guid? branchId, string key, string valueJson)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Setting key is required.", nameof(key));
        return new Setting { TenantId = tenantId, BranchId = branchId, Key = key.Trim(), ValueJson = valueJson };
    }

    public void UpdateValue(string valueJson) => ValueJson = valueJson;
}
```

Seed the two setting keys used later so the domain vocabulary is fixed now (do not write
seed data rows yet — just document the key names in `docs/ARTIFACTS.md`):
`"shop_profile"` (branch-level: name/address/phone/email/GSTIN/logo url/receipt footer),
`"invoice_series_format"` (branch-level: `"FY_CONSECUTIVE"` or `"DAILY_TIMESTAMP"`),
`"default_gst_rate"` (tenant-level, e.g. `18`), `"bank_config"` (tenant-level),
`"finance_providers"` (tenant-level, JSON array), `"repair_module_enabled"`
(tenant-level bool, mirrors `tenants.features.repairs` — Plan's `IFeatureManager`, built in
P-708, is the actual read path; this setting key is reserved but not the source of truth).

## 4. TASK

1. Create `Setting.cs` in `StoreOS.Domain/Tenancy/` exactly as shown.
2. Create `SettingConfiguration.cs` mapping `TenantId`->`tenant_id`, `BranchId`->`branch_id`,
   `Key`->`key`, `ValueJson`->`value` (column type `jsonb`). Do NOT configure a primary key
   via the fluent API for this entity (EF requires SOME key — use `.HasKey(s => new {
   s.TenantId, s.BranchId, s.Key })` as EF's own logical key for change tracking purposes;
   the REAL enforced PK in Postgres is the raw-SQL composite one with `branch_id_key` added
   via migration SQL as shown). Add `.HasIndex` is not needed since the PK covers lookups.
3. Add `DbSet<Setting> Settings` to `StoreOsDbContext`.
4. Generate migration `002_Settings`, then manually edit the generated migration's `Up()`
   method to REMOVE whatever primary key EF tried to create on the physical table (since
   `branch_id` is nullable and can't be part of a normal PK) and instead append the raw SQL
   block from the CONTEXT BLOCK that adds the `branch_id_key` generated column and the real
   composite primary key constraint. Add the mirrored raw SQL to `Down()` to drop the
   constraint and column before EF's own `DropTable`.
5. Apply the migration and confirm the table + generated column + PK exist.
6. Document the six setting key names from the CONTEXT BLOCK in `docs/ARTIFACTS.md` verbatim
   so every later prompt that reads/writes settings uses these exact strings.

## 5. HARD CONSTRAINTS

- The physical Postgres primary key MUST be
  `(tenant_id, branch_id_key, key)` using the generated `branch_id_key` column — a naive EF
  composite key on `(tenant_id, branch_id, key)` is WRONG because Postgres treats NULLs as
  distinct in unique constraints, which would allow duplicate tenant-level settings.
- Setting keys are free-text but this repo standardizes on the six names listed — do not
  invent new key names in later prompts without also updating `docs/ARTIFACTS.md`.

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
dotnet ef migrations add 002_Settings --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
docker exec storeos-postgres psql -U storeos -d storeos -c "\d settings"
# Expected: shows tenant_id, branch_id, key, value, branch_id_key columns and a PK on
# (tenant_id, branch_id_key, key).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-103 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-103 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-103` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact `Setting` class signature, migration name `002_Settings`, and the
canonical list of six setting key strings.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-103: <one-line summary>"` only after verification is
   fully green.
