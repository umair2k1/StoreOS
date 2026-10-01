# P-901 — JSON backup export (tenant/export) - all tables per Requirements Section 28

**Phase:** 9 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-901** of the StoreOS Cloud build ("JSON backup export (tenant/export) - all tables per Requirements Section 28") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 8 (Reporting/exports/PDF, P-801-809) fully complete.
- No backup/export functionality exists yet (Requirements Section 28, Plan Section 3's
  "(b) per-tenant JSON export/import to object storage").

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `IObjectStorageService` now lives in `StoreOS.Application.Abstractions` and has a second method `GetAsync` (returns `null` for a missing key; implement with `GetObjectAsync`, catching the S3 `NoSuchKey` error). `MinioObjectStorageService` (Infrastructure) implements both. Add a test for a round trip (upload, get, get-missing => null) against the MinIO container of P-002 (skip with a clear `Skip` reason when MinIO is not running, never silently pass). Ensure the buckets `storeos-media` and `storeos-backups` are created on startup if missing (`EnsureBucketsAsync`), otherwise the first upload fails on a fresh MinIO.

Create `backend/src/StoreOS.Application/Platform/BackupExport/ExportTenantDataCommand.cs`
(`IHighImpactOperation`, `settings:manage` - a full data export is sensitive enough to warrant
the live-DB permission check):

```csharp
public record ExportTenantDataCommand() : IRequest<TenantBackupDto>, IHighImpactOperation
{
    public string RequiredPermission => "settings:manage";
}
```

Define `TenantBackupDto` covering EVERY table listed in Requirements Section 28.1/Plan Appendix
B's data model summary, stamped with a schema version for forward compatibility:

```csharp
public record TenantBackupDto(
    string SchemaVersion, // "1.0" - bump this any time the backup shape changes incompatibly
    DateTimeOffset ExportedAt,
    Guid TenantId,
    object Tenant, object[] Branches, object[] Settings,
    object[] Users, object[] Roles, object[] RolePermissions, object[] UserBranches, object[] UserRoleAssignments,
    object[] Products, object[] BranchInventories, object[] Purchases, object[] InventoryImeis, object[] InventoryMovements,
    object[] Sales, object[] SaleItems, object[] CustomerPayments,
    object[] Customers, object[] CustomerLedgerEntries,
    object[] Suppliers, object[] SupplierTransactions, object[] SupplierLedgerEntries,
    object[] SupplierReturns, object[] SupplierReturnItems,
    object[] Expenses,
    object[] Liabilities, object[] LiabilityPayments,
    object[] RepairJobs, object[] RepairJobParts, object[] RepairJobStatusHistory
    // NOTE: password_hash values ARE included for Users (needed to restore login capability),
    // but are already one-way Argon2 hashes per P-301, never plaintext - this is safe to export.
);
```

`ExportTenantDataHandler.cs`: queries EVERY listed table scoped to the caller's `TenantId` (use
`IgnoreQueryFilters()` with an EXPLICIT `tenant_id` predicate for tables that would otherwise be
branch-filtered, since a FULL tenant export must include ALL branches' data regardless of the
exporting user's own branch scope - document this as another sanctioned, narrow use of
`IgnoreQueryFilters()`, alongside the ones already established in P-303/P-507/P-606/P-708),
serializes each table's rows as plain JSON objects (a simple `System.Text.Json.JsonSerializer
.SerializeToElement(entity)` per row, or project into anonymous objects matching each entity's
public shape - avoid leaking any EF-internal navigation-property cycles by either configuring
`ReferenceHandler.IgnoreCycles` or projecting to flat DTOs per table; prefer flat DTO
projections for cleanliness and to avoid accidentally exporting more than intended).

Store the resulting JSON to a timestamped file. Since S3/MinIO wiring (Plan Section 4's object
storage) is a DEPLOYMENT concern not yet built in this backend-focused manifest, for THIS
prompt, the handler RETURNS the `TenantBackupDto` directly (as a large JSON HTTP response body)
rather than uploading to object storage - note in `docs/BUILD_LOG.md` that S3/MinIO upload
wiring (using the `AWSSDK.S3` package already pinned in P-004, pointed at the P-002 MinIO dev
container) is a natural FOLLOW-UP enhancement to this prompt's `POST /tenant/export` endpoint,
but is explicitly deferred, since the manifest's Phase 9 scope per the master prompt's phase
skeleton is "Platform: backup, migration, reconciliation jobs" without an explicit S3-wiring
sub-item called out separately - implement the DIRECT-DOWNLOAD JSON response now, and flag the
S3-upload enhancement as a clearly-labeled TODO-equivalent... no, the anti-stub rule forbids
TODO comments. Instead: implement BOTH the direct-JSON-response endpoint AND a real MinIO
upload using `AWSSDK.S3` pointed at the connection details from P-002, so the feature is
COMPLETE, not deferred. Add the S3 upload as a genuine part of this prompt's scope.

Create `backend/src/StoreOS.Infrastructure/Platform/ObjectStorageService.cs`:

```csharp
using Amazon.S3;
using Amazon.S3.Model;

namespace StoreOS.Application.Abstractions;   // interface lives in Application (handlers use it); the MinIO/S3 class stays in Infrastructure

public interface IObjectStorageService
{
    Task<string> UploadAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct);
    Task<StoredObject?> GetAsync(string bucket, string key, CancellationToken ct);   // null when the key does not exist
}

public sealed record StoredObject(byte[] Content, string ContentType);

public class MinioObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _client;
    public MinioObjectStorageService(IAmazonS3 client) => _client = client;

    public async Task<string> UploadAsync(string bucket, string key, byte[] content, string contentType, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket, Key = key, InputStream = stream, ContentType = contentType
        }, ct);
        return $"{bucket}/{key}";
    }
}
```

Register the `IAmazonS3` client pointed at the P-002 MinIO endpoint
(`http://localhost:9000`, credentials `storeos_minio`/`storeos_minio_password`,
`ForcePathStyle = true` required for MinIO compatibility) in `Program.cs`. `ExportTenantDataHandler`
uploads the serialized JSON via `IObjectStorageService.UploadAsync("storeos-backups",
$"{tenantId}/backup-{timestamp}.json", ...)` in addition to returning the DTO directly.

## 4. TASK

1. Add `AWSSDK.S3` package reference to `StoreOS.Infrastructure.csproj` (already
   pinned centrally).
2. Create `IObjectStorageService.cs`/`MinioObjectStorageService.cs` in
   `StoreOS.Infrastructure/Platform/`, register the `IAmazonS3` client configured for the P-002
   MinIO dev endpoint, register the service scoped.
3. Create `ExportTenantDataCommand.cs`/`TenantBackupDto`/handler in
   `StoreOS.Application/Platform/BackupExport/`, querying and flat-projecting every listed
   table, uploading to MinIO AND returning the DTO directly.
4. Add `POST /api/tenant/export` to a new `PlatformController.cs`.
5. Write `StoreOS.IntegrationTests/Platform/ExportTenantDataTests.cs`: (a) exporting a tenant
   with seeded data across MULTIPLE branches returns ALL branches' data regardless of the
   EXPORTING user's own branch scope (proving the `IgnoreQueryFilters()` full-tenant-export
   behavior); (b) the export includes a `SchemaVersion` field; (c) `password_hash` values are
   present but are Argon2-formatted strings, never plaintext; (d) confirm a file was actually
   uploaded to the MinIO bucket by using the same `IAmazonS3` client to fetch it back and
   compare its content to the returned DTO's JSON serialization; (e) a caller without
   `settings:manage` -> 403.

## 5. HARD CONSTRAINTS

- The `IgnoreQueryFilters()` usage here is ANOTHER sanctioned, narrow exception
  (a full tenant export must see all branches regardless of the exporting user's own scope) -
  document it alongside the others already tracked in `docs/ARTIFACTS.md`.
- Every one of the ~24 listed tables must be included in the export - this is exhaustive, not a
  representative sample; missing even one table would make a subsequent restore incomplete.

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
docker-compose -f deploy/docker-compose.yml up -d minio
dotnet test backend/tests/StoreOS.IntegrationTests --filter ExportTenantDataTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-901 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-901 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-901` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `TenantBackupDto` full shape (all ~24 table arrays), the
`IObjectStorageService` interface, the `POST /api/tenant/export` route, and the MinIO bucket
name (`storeos-backups`) and key convention.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-901: <one-line summary>"` only after verification is
   fully green.
