# P-105 — RefreshTokens entity/migration

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-105** of the StoreOS Cloud build ("RefreshTokens entity/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-104 complete: `users`, `roles`, `role_permissions`, `user_branches`,
  `user_role_assignments` tables exist and migrate.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.3):

```sql
CREATE TABLE refresh_tokens (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES users(id),
  token_hash text NOT NULL,
  family_id uuid NOT NULL,
  is_revoked bool NOT NULL DEFAULT false,
  replaced_by_token_id uuid NULL REFERENCES refresh_tokens(id),
  expires_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_refresh_tokens_family ON refresh_tokens (family_id);
CREATE INDEX ix_refresh_tokens_lookup ON refresh_tokens (user_id, token_hash);
```

Create `backend/src/StoreOS.Domain/Identity/RefreshToken.cs`:

```csharp
namespace StoreOS.Domain.Identity;

public class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public Guid FamilyId { get; private set; }
    public bool IsRevoked { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private RefreshToken() { }

    public static RefreshToken IssueNew(Guid userId, string tokenHash, Guid familyId, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        return new RefreshToken
        {
            Id = Guid.NewGuid(), UserId = userId, TokenHash = tokenHash, FamilyId = familyId,
            ExpiresAt = expiresAt, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void RotateTo(Guid newTokenId)
    {
        if (IsRevoked) throw new InvalidOperationException("Token already revoked.");
        IsRevoked = true;
        ReplacedByTokenId = newTokenId;
    }

    public void Revoke() => IsRevoked = true;

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
```

The `token_hash` column stores a SHA-256 hash of the actual refresh token value (the raw
token is never persisted — only its hash, so a DB leak alone cannot be used to authenticate).
This hashing happens in `StoreOS.Infrastructure.Identity.RefreshTokenService`, built in
P-304; this prompt only creates the storage shape.

## 4. TASK

1. Create `RefreshToken.cs` exactly as shown in `StoreOS.Domain/Identity/`.
2. Create `RefreshTokenConfiguration.cs` mapping all columns to snake_case, the self-FK
   `replaced_by_token_id -> refresh_tokens.id`, and the two named indexes
   `ix_refresh_tokens_family` (on `family_id`) and `ix_refresh_tokens_lookup` (on
   `user_id, token_hash`).
3. Add `DbSet<RefreshToken> RefreshTokens` to `StoreOsDbContext`.
4. Generate migration `004_RefreshTokens` and apply it.
5. Verify the table, both indexes, and the self-referencing FK exist.

## 5. HARD CONSTRAINTS

- Never store the raw refresh token value anywhere — only
  `token_hash`. Any later prompt that touches this table must maintain this invariant.
- Do not implement the hashing algorithm or issuance logic yet — P-304 does that. This
  prompt is schema only.

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
dotnet ef migrations add 004_RefreshTokens --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
docker exec storeos-postgres psql -U storeos -d storeos -c "\d refresh_tokens"
docker exec storeos-postgres psql -U storeos -d storeos -c "\di ix_refresh_tokens_family ix_refresh_tokens_lookup"
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-105 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-105 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-105` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RefreshToken` class signature and migration name `004_RefreshTokens`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-105: <one-line summary>"` only after verification is
   fully green.
