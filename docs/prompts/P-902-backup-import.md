# P-902 — JSON restore/import (validate, staged transaction, reject partial)

**Phase:** 9 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-902** of the StoreOS Cloud build ("JSON restore/import (validate, staged transaction, reject partial)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-901 complete: full tenant export to MinIO works.
- No restore/import functionality exists yet (Requirements Section 28.2/28.3, Invariant 12).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** Backup import/restore runs through `ITenantJobRunner` (P-109) or a dedicated `IRequest` handled by `TransactionBehavior`: one transaction for the whole import, so a failed import leaves nothing behind.

Create `backend/src/StoreOS.Application/Platform/BackupImport/ImportTenantDataCommand.cs`
(`IHighImpactOperation`, `settings:manage`):

```csharp
public record ImportTenantDataCommand(TenantBackupDto Backup) : IRequest<ImportResultDto>, IHighImpactOperation
{
    public string RequiredPermission => "settings:manage";
}

public record ImportResultDto(bool Success, Dictionary<string, int> RecordCounts, string? ErrorMessage);
```

`ImportTenantDataHandler.cs` implements Requirements Section 28.2's 5 steps:

1. **Validate file structure**: confirm `Backup.SchemaVersion == "1.0"` (the only version this
   handler currently understands) - reject with a clear `unsupported_schema_version` error if
   not, WITHOUT touching the database at all.
2. **Validate supported schema/data version**: (combined with step 1 here).
3. **Preserve referential integrity**: import tables in DEPENDENCY ORDER (tenants/branches/
   settings first, then users/roles, then products, then everything else that references
   them) - since this handler operates on an EXISTING tenant (restoring INTO the CALLER's OWN
   tenant, not creating a new one - Requirements Section 28 describes disaster-recovery-style
   restore, not a NEW-tenant migration, which is P-903/904's separate concern), the import
   REPLACES the existing tenant's data. THIS IS DESTRUCTIVE - implement it as a STAGING-THEN-SWAP
   pattern per Plan Section 28.3's "transactional or use a staging database" requirement:
   perform the ENTIRE import inside ONE database transaction (the existing ambient transaction
   from `TransactionBehavior` already provides this), first DELETING all of the
   CALLER's OWN tenant's existing rows across every listed table (in REVERSE dependency order,
   children before parents, to satisfy FK constraints), THEN inserting every row from the
   backup DTO (in forward dependency order). If ANY step fails, the whole transaction rolls
   back, leaving the ORIGINAL data completely untouched (Invariant 12).
4. **Avoid partial imports**: guaranteed by the single-transaction approach.
5. **Provide a clear error if the backup is invalid**: return
   `ImportResultDto(Success: false, ErrorMessage: "...")` for validation failures caught BEFORE
   any deletion begins (schema version mismatch, malformed JSON structure) - these fail FAST,
   before touching the database at all, satisfying Plan Section 28.3's "Never partially
   overwrite a live database after a failed restore" by never starting the destructive phase
   until basic structural validation has already passed.

Implement the delete-then-insert sequence explicitly, covering all ~24 tables from P-901,
IN CORRECT DEPENDENCY ORDER for both directions. Use raw
`_db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM {tableName} WHERE tenant_id =
{scope.TenantId}")`-style calls for the deletion phase (bypassing EF change tracking for bulk
deletes, which is more efficient and avoids loading every row into memory first), and EF
`AddRangeAsync` reconstructing entities from the DTO's JSON for the insertion phase (deserialize
each `object[]` back into strongly-typed entity instances via
`JsonSerializer.Deserialize<List<EntityType>>(JsonSerializer.Serialize(rawObjectArray))` or a
more direct typed re-projection - choose whichever is cleaner in your actual implementation,
ensuring entity FACTORY validation rules don't reject legitimately-already-valid historical
data during reconstruction; if a factory method's validation is too strict for round-tripping
historical data reliably [e.g. a factory that assumes "new" semantics], add an internal
`Rehydrate(...)` factory variant on the relevant entities specifically for backup-restore
purposes, bypassing "is this a NEW valid business object" checks while still setting every
field faithfully from the backup data).

Add `POST /api/tenant/import` to `PlatformController.cs`, requiring `Idempotency-Key`.

## 4. TASK

1. Create `ImportTenantDataCommand.cs`/`ImportResultDto`/handler in
   `StoreOS.Application/Platform/BackupImport/`, implementing the full validate-then-
   delete-then-insert sequence in correct dependency order both ways.
2. Add `Rehydrate(...)` factory variants to any entity whose normal `Create`/factory validation
   would incorrectly reject legitimate historical backup data during reconstruction - identify
   these case-by-case as you implement, documenting which entities needed one and why.
3. Add `POST /api/tenant/import` to `PlatformController.cs`, mandatory `Idempotency-Key`.
4. Write `StoreOS.IntegrationTests/Platform/ImportTenantDataTests.cs`: (a) full round-trip:
   export a tenant's seeded data (P-901), then import that SAME backup back into the SAME
   tenant, and confirm every table's row COUNT matches the pre-export state exactly (a true
   round-trip test); (b) importing a backup with an unrecognized `SchemaVersion` -> fails
   cleanly with `Success: false`, and the database is COMPLETELY UNTOUCHED (verify by
   re-querying and confirming zero rows were deleted or added); (c) simulate a FAILURE
   mid-import (e.g. inject a malformed row for one table deep in the sequence) and confirm the
   ENTIRE transaction rolls back, leaving the tenant's PRE-IMPORT data fully intact,
   unchanged (Invariant 12, the definitive test); (d) a caller without `settings:manage` -> 403.

## 5. HARD CONSTRAINTS

- The import is TRANSACTIONAL end-to-end - a failure at ANY point (validation OR
  mid-delete OR mid-insert) must leave the database in EXACTLY its pre-import state, with zero
  partial changes. This is Invariant 12's core test and the single most important behavior in
  this prompt.
- Validation (schema version check) happens BEFORE any destructive operation begins - a bad
  backup file must never even start deleting existing data.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ImportTenantDataTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-902 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-902 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-902` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ImportTenantDataCommand`/`ImportResultDto` shapes, the
`POST /api/tenant/import` route, the full dependency-ordered delete/insert table sequence, and
which entities needed a `Rehydrate(...)` factory variant.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-902: <one-line summary>"` only after verification is
   fully green.
