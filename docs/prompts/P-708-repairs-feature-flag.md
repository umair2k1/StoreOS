# P-708 — Repair feature flag (IFeatureManager wrapper over tenants.features jsonb)

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-708** of the StoreOS Cloud build ("Repair feature flag (IFeatureManager wrapper over tenants.features jsonb)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-707 complete: full repair lifecycle including billing works
  end-to-end.
- `tenants.features jsonb` column exists (P-102) but nothing reads or writes it yet. Per Plan
  Section 5.1, repair-module enablement must go through a proper `IFeatureManager` abstraction,
  never raw dictionary checks scattered through handlers.

## 3. CONTEXT BLOCK

Add the `Microsoft.FeatureManagement` package (already pinned in P-004)
to `StoreOS.Infrastructure.csproj` if not already present.

Create `backend/src/StoreOS.Application/Platform/IFeatureManager.cs`:

```csharp
namespace StoreOS.Application.Platform;

public interface IFeatureManager
{
    Task<bool> IsRepairsEnabledAsync(CancellationToken ct);
    Task SetRepairsEnabledAsync(bool enabled, CancellationToken ct);
}
```

Implementation `backend/src/StoreOS.Infrastructure/Platform/TenantFeatureManager.cs` reads/
writes the `tenants.features` jsonb column directly (a thin, tenant-scoped wrapper - this does
NOT need the full `Microsoft.FeatureManagement` library's feature-flag-provider abstraction
for a single boolean; that library is more useful for RELEASE TOGGLES per Plan Section 5.1's
distinction between "Business Feature Flags" (this one, tenant-controlled, stored in
`tenants.features`) and "Release Toggles" (developer/deployment-controlled, via
appsettings/environment variables, NOT built in this prompt since none exist yet) - implement
`TenantFeatureManager` as a direct, simple EF-backed reader/writer, and reserve
`Microsoft.FeatureManagement`'s library usage for a FUTURE prompt if a genuine release-toggle
need arises; do not force it in artificially here just because the package is pinned):

```csharp
public class TenantFeatureManager : IFeatureManager
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    public TenantFeatureManager(StoreOsDbContext db, IAccessScope scope) { _db = db; _scope = scope; }

    public async Task<bool> IsRepairsEnabledAsync(CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == _scope.TenantId, ct);
        using var doc = System.Text.Json.JsonDocument.Parse(tenant.FeaturesJson);
        return doc.RootElement.TryGetProperty("repairs", out var val) && val.GetBoolean();
    }

    public async Task SetRepairsEnabledAsync(bool enabled, CancellationToken ct)
    {
        var tenant = await _db.Tenants.IgnoreQueryFilters()
            .FirstAsync(t => t.Id == _scope.TenantId, ct);
        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, bool>>(tenant.FeaturesJson)
            ?? new Dictionary<string, bool>();
        dict["repairs"] = enabled;
        tenant.SetFeatures(System.Text.Json.JsonSerializer.Serialize(dict));
    }
}
```

Note the `IgnoreQueryFilters()` here reads `tenants` directly by the CALLER's own
`_scope.TenantId` (never a foreign tenant) - this is a narrow, safe, and necessary use since
`Tenant` itself has no query filter defined at all (per P-107, only `Branch`/`Setting` got
filters; `Tenant` was never filtered because a tenant can only ever legitimately read ITS OWN
row via its own scope's `TenantId`, making a query filter on `Tenant` somewhat moot but not
harmful - add one now for defense-in-depth consistency: `modelBuilder.Entity<Tenant>()
.HasQueryFilter(t => t.Id == scope.TenantId);` in `QueryFilters.ApplyAll`, and update this
prompt's `TenantFeatureManager`/`GetMe`/any other `Tenant`-reading code to NOT need
`IgnoreQueryFilters()` anymore once this filter is added, since it now naturally resolves only
the caller's own tenant).

Add a MediatR pipeline behavior OR an explicit handler-level check pattern for feature-gating
repair endpoints. Simpler: since `IFeatureManager` is now available, have EVERY repair-related
handler (P-701-707's handlers) call `await _featureManager.IsRepairsEnabledAsync(ct)` at the
start and throw `DomainValidationException("repairs_module_disabled", ...)` if false - this
requires RETROFITTING every prior repair handler with this check. To avoid touching 15+ files,
implement it instead as a MediatR behavior gated on a new marker interface
`IRequiresRepairsFeature`, applied to EVERY repair command/query from P-701 through P-707:

```csharp
namespace StoreOS.Application.Abstractions;
public interface IRequiresRepairsFeature { }
```

```csharp
public class RepairsFeatureGateBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IFeatureManager _featureManager;
    public RepairsFeatureGateBehavior(IFeatureManager featureManager) => _featureManager = featureManager;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is IRequiresRepairsFeature && !await _featureManager.IsRepairsEnabledAsync(ct))
            throw new DomainValidationException("repairs_module_disabled", "The repairs module is not enabled for this tenant.");
        return await next();
    }
}
```

Register it in the pipeline, and retrofit every repair-related command/query from P-701-707
(all of them: CreateRepairService, GetRepairServices, UpdateRepairService,
DeactivateRepairService, CreateRepairJob, GetRepairJobs, GetRepairJobById,
UpdateRepairJobDiagnosis, UpdateRepairJobCommercials, ChangeRepairStatus,
GetRepairStatusHistory, ReservePart, ConsumePart, ReturnPart, GetRepairJobParts,
RecordApproval, DeliverRepairJob, BillRepair) to ALSO implement
`IRequiresRepairsFeature` alongside their existing `IRequiresPermission`/`IHighImpactOperation`
interface.

Create `StoreOS.Application/Settings/RepairFeature/SetRepairsEnabledCommand.cs`
(`IRequiresPermission`, `settings:manage` - deliberately NOT gated by
`IRequiresRepairsFeature` itself, since you must be able to ENABLE the feature even while it's
currently disabled). Add `GET/PATCH /api/settings/repairs-enabled` to `SettingsController.cs`.

## 4. TASK

1. Add the `Tenant` query filter to `QueryFilters.ApplyAll` as described, and
   remove the now-unnecessary `IgnoreQueryFilters()` calls from any handler that reads the
   caller's own tenant row (re-check `LoginHandler`/`RefreshHandler` from Phase 3 too - those
   read a tenant by an EXPLICIT `TenantId` parameter BEFORE authentication, so they still
   legitimately need `IgnoreQueryFilters()` since no `IAccessScope` exists yet at that point;
   leave those two alone, only simplify POST-AUTHENTICATION tenant reads).
2. Create `IFeatureManager.cs`/`TenantFeatureManager.cs` exactly as shown, register scoped.
3. Create `IRequiresRepairsFeature.cs` marker and `RepairsFeatureGateBehavior.cs`, register in
   the MediatR pipeline.
4. Retrofit ALL 17 listed repair-related commands/queries from P-701 through P-707 to also
   implement `IRequiresRepairsFeature`.
5. Create `SetRepairsEnabledCommand.cs`/handler and add the two settings routes.
6. Write `StoreOS.IntegrationTests/Repairs/RepairsFeatureFlagTests.cs`: (a) with the feature
   DISABLED (tenant default), every one of the 17 repair endpoints returns
   `repairs_module_disabled` (422) instead of executing; (b) enabling the feature via
   `PATCH /api/settings/repairs-enabled` immediately allows all 17 to function normally; (c)
   disabling it again does NOT delete or hide any EXISTING repair job/history/parts data -
   directly query the database and confirm all rows are still present even while the feature
   flag is off (only the API surface is gated, never the underlying data, per Requirements
   Section 36.6's "Disabling the feature should hide or disable repair-specific workflows
   without deleting repair history").

## 5. HARD CONSTRAINTS

- ALL 17 repair-related commands/queries must implement
  `IRequiresRepairsFeature` - a repository-wide grep for
  `StoreOS.Application/Repairs/` and `StoreOS.Application/Settings/FinanceProviders` (no, that
  one's unrelated) confirming every repair use-case folder's command/query classes carry this
  marker is a required self-check before finishing.
- Disabling the feature NEVER deletes, archives, or hides existing rows at the DATABASE level -
  it only gates the API surface going forward. Historical repair jobs, parts, and status
  history remain fully intact and queryable directly in the database regardless of the flag.

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
dotnet build StoreOS.sln
dotnet test backend/tests/StoreOS.IntegrationTests --filter RepairsFeatureFlagTests
dotnet test backend/tests/StoreOS.IntegrationTests
# Expected: new tests pass; full existing suite remains green (the Tenant query-filter
# addition and IgnoreQueryFilters cleanup must not regress any prior test).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-708 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-708 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-708` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IFeatureManager` interface, the `IRequiresRepairsFeature` marker, the
full list of 17 retrofitted repair use-cases, and confirmation the `Tenant` query filter is now
active (simplifying post-auth tenant reads).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-708: <one-line summary>"` only after verification is
   fully green.
