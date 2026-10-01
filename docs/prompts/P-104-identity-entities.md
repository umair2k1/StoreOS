# P-104 — Users, Roles, RolePermissions, UserBranches, UserRoleAssignments entities/migration

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-104** of the StoreOS Cloud build ("Users, Roles, RolePermissions, UserBranches, UserRoleAssignments entities/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-102, P-103 complete: tenants/branches/settings tables exist and migrate
  cleanly.
- No identity tables exist yet. This prompt builds the full RBAC schema from Plan §8.3
  (minus refresh_tokens, which is P-105).

## 3. CONTEXT BLOCK

Target DDL (Plan §8.3, minus refresh_tokens):

```sql
CREATE TABLE users (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  email text NOT NULL,
  password_hash text NOT NULL,
  display_name text NOT NULL,
  token_version int NOT NULL DEFAULT 1,
  is_active bool NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, email)
);

CREATE TABLE roles (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL
);

CREATE TABLE role_permissions (
  role_id uuid NOT NULL REFERENCES roles(id),
  permission_code text NOT NULL,
  PRIMARY KEY (role_id, permission_code)
);

CREATE TABLE user_branches (
  user_id uuid NOT NULL REFERENCES users(id),
  branch_id uuid NOT NULL REFERENCES branches(id),
  is_default bool NOT NULL DEFAULT false,
  PRIMARY KEY (user_id, branch_id)
);

CREATE TABLE user_role_assignments (
  user_id uuid NOT NULL REFERENCES users(id),
  role_id uuid NOT NULL REFERENCES roles(id),
  branch_id uuid NULL REFERENCES branches(id),
  PRIMARY KEY (user_id, role_id, branch_id)
);
```

Note: like `settings.branch_id`, `user_role_assignments.branch_id` is nullable and part of a
composite PK — apply the SAME `branch_id_key` generated-column technique used in P-103 to
make the physical PK `(user_id, role_id, branch_id_key)` so two NULL-branch (tenant-wide)
assignments of the same role to the same user can't silently duplicate, while still allowing
exactly one tenant-wide assignment vs. many per-branch ones.

Create entities in `StoreOS.Domain/Identity/`: `User.cs`, `Role.cs`, `RolePermission.cs`
(or a simple join handled as a value collection — prefer an explicit entity class for
clarity), `UserBranch.cs`, `UserRoleAssignment.cs`. Follow the same style as `Tenant`/`Branch`
in P-102: private setters, static factories, behavior methods. Example for `User`:

```csharp
namespace StoreOS.Domain.Identity;

public class User
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Email { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public string DisplayName { get; private set; } = default!;
    public int TokenVersion { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { }

    public static User Create(Guid tenantId, string email, string passwordHash, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("Password hash required.", nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Display name required.", nameof(displayName));
        return new User
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash, DisplayName = displayName.Trim(), CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void ChangePasswordHash(string newHash)
    {
        if (string.IsNullOrWhiteSpace(newHash)) throw new ArgumentException("Password hash required.", nameof(newHash));
        PasswordHash = newHash;
    }

    public void BumpTokenVersion() => TokenVersion++;
    public void Deactivate() { IsActive = false; BumpTokenVersion(); }
    public void Activate() => IsActive = true;
}
```

`Role.cs`: `Id`, `TenantId`, `Name`, static `Create(tenantId, name)`, `Rename(name)`.

`RolePermission.cs`: `RoleId`, `PermissionCode` (string), static `Create(roleId, code)`.

`UserBranch.cs`: `UserId`, `BranchId`, `IsDefault` (bool), static `Create(userId, branchId,
isDefault)`, `SetDefault(bool)`.

`UserRoleAssignment.cs`: `UserId`, `RoleId`, `BranchId` (nullable Guid — null = tenant-wide),
static `Create(userId, roleId, branchId)`.

## 4. TASK

1. Create all five entity classes in `StoreOS.Domain/Identity/` exactly per the
   patterns shown (private setters, static factories, validation in factories).
2. Create EF configurations for each in
   `StoreOS.Infrastructure/Persistence/Configurations/`, mapping every column to explicit
   snake_case names, all FKs, and:
   - `users`: unique index on `(tenant_id, email)`.
   - `role_permissions`: composite key `(role_id, permission_code)`.
   - `user_branches`: composite key `(user_id, branch_id)`.
   - `user_role_assignments`: apply the `branch_id_key` generated-column technique from
     P-103 via raw SQL in the migration, physical PK `(user_id, role_id, branch_id_key)`.
3. Add all five `DbSet<T>` properties to `StoreOsDbContext`.
4. Generate migration `003_Identity`, manually patch the `Up()`/`Down()` for the
   `user_role_assignments` generated column + PK exactly as done for `settings` in P-103.
5. Apply the migration and verify all five tables plus their constraints via `\d` in psql.

## 5. HARD CONSTRAINTS

- `users.email` must be lowercased and trimmed at creation time
  (done in the `User.Create` factory) so the `(tenant_id, email)` unique index is
  case-insensitively enforced at the application layer (Postgres `UNIQUE` on `text` is
  case-sensitive; normalizing in the factory is the chosen mitigation — do not add a
  `citext` extension dependency).
- `password_hash` must never be logged or included in any DTO — this is enforced starting in
  P-301, but do not add any `ToString()` override or logging call that could leak it now.
- Do not implement JWT issuance, login, or permission checks yet — this prompt is schema
  only.

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
dotnet ef migrations add 003_Identity --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
for t in users roles role_permissions user_branches user_role_assignments; do
  docker exec storeos-postgres psql -U storeos -d storeos -c "\d $t"
done
# Expected: all five tables described with correct columns/keys/FKs.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-104 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-104 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-104` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: all five entity class signatures, migration name `003_Identity`, and the
exact composite-key shape of `user_role_assignments` (including the `branch_id_key`
generated column pattern, since P-115/P-308 will query this table directly).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-104: <one-line summary>"` only after verification is
   fully green.
