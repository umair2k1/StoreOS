# P-311 — Users/roles/branch-assignment admin endpoints

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-311** of the StoreOS Cloud build ("Users/roles/branch-assignment admin endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-310 complete: Branches CRUD works.
- No admin endpoints exist for creating users, roles, or assigning roles/branches to users —
  the only user in the system so far (if any) would have to be seeded directly via raw SQL/EF
  in a test, which is untenable for real onboarding. This prompt closes that gap.

## 3. CONTEXT BLOCK

Create the vertical slices under `StoreOS.Application/Identity/Users/`:
`CreateUser` (`IHighImpactOperation`, `users:manage`; takes email/displayName/password/initial
role+branch assignment; hashes the password via `IPasswordHasher` from P-301 before calling
`User.Create`), `GetUsers` (list, `users:manage` required since it exposes emails/roles —
actually make this `IRequiresPermission` not `IHighImpactOperation`, since listing users is
sensitive but not "high-impact" in the destructive sense Plan §8.3 defines — use the normal
cached-claims check here), `AssignRole` (`IHighImpactOperation`, `users:manage`; inserts a
`UserRoleAssignment` row, validates the `RoleId` belongs to the same tenant and the permission
codes on that role are all in `PermissionCatalog.All` per P-115), `AssignBranch`
(`IHighImpactOperation`, `users:manage`; inserts/updates a `UserBranch` row, sets
`IsDefault`), `DeactivateUser` (`IHighImpactOperation`, `users:manage`; calls
`user.Deactivate()` which also bumps `token_version`, immediately invalidating their session
per P-305/P-307/P-308's combined mechanisms).

Also create `StoreOS.Application/Identity/Roles/CreateRole/CreateRoleCommand.cs`
(`IHighImpactOperation`, `users:manage`; takes `Name` + a list of permission codes; validates
EVERY code via `IPermissionChecker.IsValidPermissionCode` from P-115, rejecting the whole
request with `DomainValidationException("invalid_permission_code", ...)` if any code is
unrecognized — this is the enforcement point promised back in P-115's task list) and
`GetRoles/GetRolesQuery.cs`.

Example `CreateRoleHandler.cs` illustrating the catalog validation:

```csharp
public class CreateRoleHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IPermissionChecker _permissionChecker;
    public CreateRoleHandler(StoreOsDbContext db, IAccessScope scope, IPermissionChecker permissionChecker)
    { _db = db; _scope = scope; _permissionChecker = permissionChecker; }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var invalid = request.PermissionCodes.Where(c => !_permissionChecker.IsValidPermissionCode(c)).ToList();
        if (invalid.Count > 0)
            throw new DomainValidationException("invalid_permission_code",
                $"Unknown permission code(s): {string.Join(", ", invalid)}");
        var role = Role.Create(_scope.TenantId, request.Name);
        await _db.Set<Role>().AddAsync(role, ct);
        foreach (var code in request.PermissionCodes.Distinct())
            await _db.Set<RolePermission>().AddAsync(RolePermission.Create(role.Id, code), ct);
        await _db.SaveChangesAsync(ct);
        return role.Id;
    }
}
```

Create `backend/src/StoreOS.Api/Controllers/UsersController.cs` and
`backend/src/StoreOS.Api/Controllers/RolesController.cs` wiring all the above as REST
endpoints (`POST/GET /api/users`, `POST /api/users/{id}/roles`,
`POST /api/users/{id}/branches`, `POST /api/users/{id}/deactivate`,
`POST/GET /api/roles`).

## 4. TASK

1. Create all six vertical slices (`CreateUser`, `GetUsers`, `AssignRole`,
   `AssignBranch`, `DeactivateUser` under `Identity/Users/`; `CreateRole`, `GetRoles` under
   `Identity/Roles/`) following the established folder convention, each with
   command/query+validator+handler.
2. Create `UsersController.cs` and `RolesController.cs` exactly matching the described routes.
3. Add `invalid_permission_code` -> 400 mapping to `ExceptionHandlingMiddleware`.
4. Write `StoreOS.IntegrationTests/Identity/UsersAdminTests.cs`: (a) create a role with valid
   permission codes -> 201; (b) create a role with an unknown code -> 400
   `invalid_permission_code`; (c) create a user, assign the role tenant-wide (`branch_id=null`)
   -> the user's SUBSEQUENT login (via P-303) issues a JWT carrying that role's permissions;
   (d) assign a branch to a user with `isDefault=true` -> `GET /api/users` (or a per-user
   detail endpoint) reflects it; (e) deactivate a user -> `token_version` bumped, and (reusing
   P-307/P-308's mechanisms) any of their subsequently-tested existing JWTs are rejected; (f) a
   caller WITHOUT `users:manage` cannot create users/roles/assignments -> 403 in each case.

## 5. HARD CONSTRAINTS

- Every permission code accepted into `role_permissions` must be
  validated against `PermissionCatalog.All` (via `IPermissionChecker`) at write time — never
  allow an arbitrary string into this table, since the two live-DB/cached permission-check
  behaviors trust whatever is stored there.
- `CreateUser` must use `IPasswordHasher.Hash(...)` (P-301) before calling `User.Create` —
  never accept or store a plaintext password anywhere in this flow.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter UsersAdminTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-311 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-311 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-311` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: all six use-case signatures, the `UsersController`/`RolesController` routes,
and the `invalid_permission_code` validation enforcement point.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-311: <one-line summary>"` only after verification is
   fully green.
