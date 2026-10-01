# P-116 — audit_log table/migration + IAuditWriter service

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-116** of the StoreOS Cloud build ("audit_log table/migration + IAuditWriter service") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-109 (transaction scope), P-112 (correlation ID), P-115 (permission
  catalog) complete.
- No audit trail exists yet. Per Plan §6.5, the audit_log table must be append-only (DB role
  has no UPDATE/DELETE grant on it — that grant restriction is a deployment/IaC concern noted
  for P-1205's final audit and not enforceable purely via migration SQL in a dev Postgres
  superuser setup, but the APPLICATION layer must never attempt to update or delete audit rows).

## 3. CONTEXT BLOCK

Target DDL (Plan §8.10):

```sql
CREATE TABLE audit_log (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NULL,
  correlation_id text NULL,
  occurred_at timestamptz NOT NULL DEFAULT now(),
  user_id uuid,
  action text NOT NULL,
  entity_type text NOT NULL,
  entity_id text NOT NULL,
  changes jsonb
);
CREATE INDEX ix_audit_log_tenant_correlation ON audit_log (tenant_id, correlation_id) WHERE correlation_id IS NOT NULL;
```

Create `backend/src/StoreOS.Domain/Platform/AuditLogEntry.cs`:

```csharp
namespace StoreOS.Domain.Platform;

public class AuditLogEntry
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string EntityType { get; private set; } = default!;
    public string EntityId { get; private set; } = default!;
    public string? ChangesJson { get; private set; }

    private AuditLogEntry() { }

    public static AuditLogEntry Create(Guid tenantId, Guid? branchId, string? correlationId,
        Guid? userId, string action, string entityType, string entityId, string? changesJson)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("EntityType is required.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId)) throw new ArgumentException("EntityId is required.", nameof(entityId));
        return new AuditLogEntry
        {
            TenantId = tenantId, BranchId = branchId, CorrelationId = correlationId, UserId = userId,
            Action = action, EntityType = entityType, EntityId = entityId, ChangesJson = changesJson,
            OccurredAt = DateTimeOffset.UtcNow
        };
    }
}
```

Application-layer writer `backend/src/StoreOS.Application/Platform/IAuditWriter.cs`:

```csharp
namespace StoreOS.Application.Platform;

public interface IAuditWriter
{
    Task WriteAsync(Guid? branchId, string action, string entityType, string entityId,
        object? changes, CancellationToken ct = default);
}
```

Implementation `backend/src/StoreOS.Infrastructure/Platform/AuditWriter.cs` resolves
`TenantId`/`UserId` from `IAccessScope` and `CorrelationId` from
`ICorrelationContextAccessor`, serializes `changes` with `System.Text.Json`, and adds the
entity to the AMBIENT `StoreOsDbContext` (so it is written within the SAME transaction as the
business operation that triggered it — never a separate connection/transaction, or it could
persist even if the surrounding operation rolls back, or vice versa):

```csharp
using System.Text.Json;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Platform;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Platform;

public class AuditWriter : IAuditWriter
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    private readonly ICorrelationContextAccessor _correlation;

    public AuditWriter(StoreOsDbContext db, IAccessScope scope, ICorrelationContextAccessor correlation)
    {
        _db = db; _scope = scope; _correlation = correlation;
    }

    public async Task WriteAsync(Guid? branchId, string action, string entityType, string entityId,
        object? changes, CancellationToken ct = default)
    {
        var changesJson = changes is null ? null : JsonSerializer.Serialize(changes);
        var entry = AuditLogEntry.Create(_scope.TenantId, branchId, _correlation.CorrelationId,
            _scope.UserId, action, entityType, entityId, changesJson);
        await _db.AuditLogEntries.AddAsync(entry, ct);
    }
}
```

Note this only ADDS the entity to the change tracker — it does NOT call `SaveChangesAsync()`
itself. The caller's own unit-of-work (typically the same handler that also saves the business
entity) is responsible for a single `SaveChangesAsync()` that persists both together
atomically.

## 4. TASK

1. Create `AuditLogEntry.cs` in `StoreOS.Domain/Platform/` exactly as shown.
2. Create the EF configuration mapping every column snake_case, `Id` as
   `ValueGeneratedOnAdd()` matching `GENERATED ALWAYS AS IDENTITY`, and the partial index
   `ix_audit_log_tenant_correlation` (`.HasIndex(...).HasFilter("correlation_id IS NOT NULL")`).
3. Add `DbSet<AuditLogEntry> AuditLogEntries` to `StoreOsDbContext`. Apply the RLS
   Archetype B pattern... actually `audit_log` has `branch_id` NULLABLE (head-office-style
   entries e.g. tenant-level actions), so use the "nullable BranchId" query-filter and RLS
   variant documented in P-107/P-110: `tenant_id = current_setting(...) AND (branch_id IS
   NULL OR all_branches OR branch_id = ANY(...))`. Apply this exact policy in the migration.
4. Generate migration `007_AuditLog`, including the RLS policy SQL, apply it.
5. Create `IAuditWriter.cs` in `StoreOS.Application/Platform/` and `AuditWriter.cs` in
   `StoreOS.Infrastructure/Platform/` exactly as shown; register scoped.
6. Write `StoreOS.IntegrationTests/Platform/AuditWriterTests.cs`: using the `TestAuthHandler`
   to simulate an authenticated tenant/branch context, call `IAuditWriter.WriteAsync(...)`
   followed by `_db.SaveChangesAsync()`, then query `AuditLogEntries` and assert the row
   exists with the correct `tenant_id`, `correlation_id` (matching the request's), `user_id`,
   and `changes` JSON round-trips correctly.
7. Re-run `RlsCoverageTests` (P-111) and `RlsBranchScopedPolicyTests`-style checks to confirm
   `audit_log` is now RLS-protected too.

## 5. HARD CONSTRAINTS

- `AuditWriter.WriteAsync` must NEVER call `SaveChangesAsync()`
  itself — it only stages the entry via `AddAsync`. This guarantees the audit row commits or
  rolls back atomically with whatever business operation triggered it, honoring Requirements
  Invariant 11.
- The application's runtime DB role should have no UPDATE/DELETE grant on `audit_log` in
  production — flag this explicitly as an IaC/deployment task for P-1205's final audit
  checklist; do not attempt to enforce it via a migration in this dev-focused prompt (revoking
  grants from the same role used for `dotnet ef database update` would break the migration
  tooling itself in this simplified dev setup).

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
dotnet ef migrations add 007_AuditLog --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter AuditWriterTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
# Expected: all green; audit_log now counted among RLS-protected tables.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-116 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-116 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-116` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `AuditLogEntry` signature, `IAuditWriter` interface signature (note: it
does NOT call SaveChanges itself — callers must), and the migration name `007_AuditLog`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-116: <one-line summary>"` only after verification is
   fully green.
