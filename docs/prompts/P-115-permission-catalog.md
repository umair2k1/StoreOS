# P-115 — Permission catalog seed + IPermissionChecker

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-115** of the StoreOS Cloud build ("Permission catalog seed + IPermissionChecker") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-114 complete: MediatR pipeline enforces `IRequiresPermission` via
  `_scope.HasPermission(code)`, but the SET of valid permission codes is not yet formally
  defined anywhere — any string currently "works" as a permission code, which is fragile.
- `role_permissions.permission_code` (P-104) is a free-text column with no referential
  integrity to a fixed catalog. This prompt fixes that by defining the canonical list and
  validating against it at role-assignment time (enforced starting in P-311, referenced here).

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Domain/Identity/PermissionCatalog.cs` with the
EXACT permission codes from Plan §6.2's table (this is the complete, final list — do not add
or remove codes without updating this file and this prompt's record in `docs/ARTIFACTS.md`):

```csharp
namespace StoreOS.Domain.Identity;

public static class PermissionCatalog
{
    public const string PosCheckout = "pos:checkout";
    public const string SalesDiscountApply = "sales:discount:apply";
    public const string SalesVoid = "sales:void";
    public const string SalesRefund = "sales:refund";
    public const string SalesCreditOverride = "sales:credit:override";
    public const string SalesReadAnyBranch = "sales:read:any-branch";
    public const string InventoryCostView = "inventory:cost:view";
    public const string ReportsProfitView = "reports:profit:view";
    public const string InventoryManage = "inventory:manage";
    public const string PurchasesManage = "purchases:manage";
    public const string InventoryAdjustments = "inventory:adjustments";
    public const string ExpensesManage = "expenses:manage";
    public const string LiabilitiesManage = "liabilities:manage";
    public const string RepairsManage = "repairs:manage";
    public const string SettingsManage = "settings:manage";
    public const string UsersManage = "users:manage";
    public const string BranchesManage = "branches:manage";

    public static readonly IReadOnlyList<string> All = new[]
    {
        PosCheckout, SalesDiscountApply, SalesVoid, SalesRefund, SalesCreditOverride,
        SalesReadAnyBranch, InventoryCostView, ReportsProfitView, InventoryManage,
        PurchasesManage, InventoryAdjustments, ExpensesManage, LiabilitiesManage,
        RepairsManage, SettingsManage, UsersManage, BranchesManage
    };

    public static bool IsValid(string code) => All.Contains(code);
}
```

Create `backend/src/StoreOS.Application/Identity/IPermissionChecker.cs`:

```csharp
namespace StoreOS.Application.Identity;

public interface IPermissionChecker
{
    bool IsValidPermissionCode(string code);
    IReadOnlyList<string> AllCodes { get; }
}
```

Implementation `backend/src/StoreOS.Infrastructure/Identity/PermissionChecker.cs` simply
delegates to `PermissionCatalog` (thin adapter so `StoreOS.Application` doesn't reference
`StoreOS.Domain.Identity` directly if that violates a layering preference — actually
`StoreOS.Application` already references `StoreOS.Domain` per P-001's project references, so
this indirection is for testability, not a hard layering requirement):

```csharp
using StoreOS.Application.Identity;
using StoreOS.Domain.Identity;

namespace StoreOS.Infrastructure.Identity;

public class PermissionChecker : IPermissionChecker
{
    public bool IsValidPermissionCode(string code) => PermissionCatalog.IsValid(code);
    public IReadOnlyList<string> AllCodes => PermissionCatalog.All;
}
```

Register `builder.Services.AddSingleton<IPermissionChecker, PermissionChecker>();` (the
catalog is compile-time constant, safe as a singleton).

## 4. TASK

1. Create `PermissionCatalog.cs` in `StoreOS.Domain/Identity/` with exactly the 17
   permission codes listed.
2. Create `IPermissionChecker.cs` in `StoreOS.Application/Identity/` and
   `PermissionChecker.cs` in `StoreOS.Infrastructure/Identity/` exactly as shown, register as
   singleton.
3. Write `StoreOS.Domain.Tests/Identity/PermissionCatalogTests.cs` asserting `All.Count == 17`
   and that every constant string matches the Plan §6.2 table exactly (list them all
   explicitly in the test, do not just count).
4. Write `StoreOS.IntegrationTests/Identity/PermissionCheckerTests.cs` confirming
   `IsValidPermissionCode("pos:checkout") == true` and
   `IsValidPermissionCode("not:a:real:permission") == false`.

## 5. HARD CONSTRAINTS

- This 17-code list is the COMPLETE and FINAL permission catalog for
  the entire product. No later prompt may introduce a new permission code without it first
  being added to `PermissionCatalog.All` in a dedicated small follow-up — if you find a later
  prompt in this manifest referencing a permission code not in this list, treat that as a
  manifest defect and use the closest matching existing code instead, noting the discrepancy
  in `docs/BUILD_LOG.md`.
- Do not enforce catalog validity at the DATABASE level in this prompt (no CHECK constraint on
  `role_permissions.permission_code` yet) — that enforcement happens at the application layer
  in P-311 when roles are assigned permissions via the admin endpoint.

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
dotnet test backend/tests/StoreOS.Domain.Tests --filter PermissionCatalogTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter PermissionCheckerTests
# Expected: both green.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-115 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-115 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-115` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete list of all 17 permission code constants and their exact string
values, since every later prompt that gates an endpoint quotes one of these verbatim.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-115: <one-line summary>"` only after verification is
   fully green.
