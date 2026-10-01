# P-313 — Change own password + admin password reset

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-313** of the StoreOS Cloud build ("Change own password + admin password reset") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document. Read `prompts/_shared/CONTRACTS.md` and `prompts/_shared/DECISIONS.md` first.

## 2. WORLD STATE

- P-301 (`IPasswordHasher`), P-304/P-305 (refresh tokens, families), P-307 (`token_version` check), P-311 (users admin), P-312 (rate limiting), P-116 (`IAuditWriter`) are complete.
- The original build had NO way to change a password after `CreateUser`, and no way for an owner to reset a forgotten one. Users of a shop would be stuck with the initial password. (E-mail based 'forgot password' is out of scope: the plan contains no mail service.)

## 3. CONTEXT BLOCK

Two commands under `StoreOS.Application/Identity/Password/` (vertical slices, one folder each: `ChangePassword/`, `AdminResetPassword/`), both implemented like the existing P-311 handlers (IAppDbContext, `IPasswordHasher`, `IAuditWriter`):

1. `ChangePasswordCommand(string CurrentPassword, string NewPassword)` — any authenticated user, NOT high-impact. Handler: load the caller (`_scope.UserId`), `Verify(current)` else `DomainException("invalid_credentials")` (mapped 401 by `ErrorCatalog`... use code `current_password_incorrect` (add it to `ErrorCatalog.Explicit` as 422) so the UI can show a field error without triggering the refresh flow), validate the new password with the SAME rules `CreateUser` uses (P-311/P-301; if no rule exists yet: minimum 10 characters, at least one letter and one digit, must differ from the current password), `ChangePasswordHash(hash)`, `BumpTokenVersion()`, revoke ALL refresh tokens of the user (`is_revoked = true` for every family), write audit `user.password.changed`.
   Endpoint `POST /api/auth/change-password` (`[Authorize]`, rate limited like login: 5/min/user). Response: 204 and the refresh cookie is CLEARED (the user logs in again; the current access token dies within seconds through the `token_version` check).
2. `AdminResetPasswordCommand(Guid UserId, string NewTemporaryPassword)` — `IHighImpactOperation`, permission `users:manage`. Sets the hash, bumps `token_version`, revokes all refresh tokens, audit `user.password.reset` (never log the password). Owner accounts may only be reset by another Owner (`tenant-wide` role assignment); otherwise `forbidden`.
   Endpoint `POST /api/users/{id}/reset-password`. Response 204.
3. Frontend follow-up (documented only; implemented by P-1120/P-1121, see their revision notes): a 'Change password' dialog on the account menu and a 'Reset password' action in the users admin table.

## 4. TASK

1. Implement the two slices with FluentValidation validators (`NewPassword` rules above; `CurrentPassword` not empty; `NewTemporaryPassword` same rules), controllers, DI registrations, `ErrorCatalog` entry for `current_password_incorrect`.
2. Tests (`PasswordTests`, integration, app role): (a) change with correct current password succeeds, old password no longer logs in, new one does; (b) wrong current password => 422 `current_password_incorrect`; (c) after a change the OLD access token is rejected within one request (token_version) and the OLD refresh cookie cannot refresh; (d) admin reset by a user WITHOUT `users:manage` => 403 `forbidden`; (e) Branch Manager cannot reset an Owner; (f) both operations write an audit row with the correlation id and NO password material in `changes`; (g) weak passwords are rejected with field errors (400).

## 5. HARD CONSTRAINTS

- Passwords are never logged, never returned, never stored except through `IPasswordHasher`.
- Every session of the user must die on change/reset (token_version bump + refresh-token revocation), otherwise a stolen session survives a password change.
- Admin reset is a high-impact operation: live DB permission check (P-308), not JWT claims.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter "FullyQualifiedName~PasswordTests"
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-313 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-313 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-313` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the two command signatures, the two routes, the audit action names and the new error code.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-313: <one-line summary>"` only after verification is
   fully green.
