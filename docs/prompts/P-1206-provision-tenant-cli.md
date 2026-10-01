# P-1206 — Tenant provisioning CLI (tools/provision) — first real tenant

**Phase:** 12 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1206** of the StoreOS Cloud build ("Tenant provisioning CLI (tools/provision) — first real tenant") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document. Read `prompts/_shared/CONTRACTS.md` and `prompts/_shared/DECISIONS.md` first.

## 2. WORLD STATE

- The whole backend is built; P-1201 created `tools/seed` for DEMO data only. There is still no supported way to create a REAL tenant: `tenants`, `roles`, `role_permissions`, the first branch and the first Owner user all sit behind RLS, and the app role (`storeos_app`) cannot bootstrap them.
- Plan §12.1 says the first-run wizard 'saves `tenants.name` and initializes the branch settings' — the tenant itself and its Owner must exist before anyone can log in.

## 3. CONTEXT BLOCK

`tools/provision` (console project, `net10.0` per toolchain decision O-03) references Domain, Application, Infrastructure like `tools/seed`. It connects with the OWNER connection (`STOREOS_MIGRATIONS_CONNECTION`), because provisioning legitimately creates rows for a tenant that does not exist yet. It must NEVER be included in the API Docker image (P-003) and is run by an operator, not by the API.

Command line: `dotnet run --project tools/provision -- --name "Apex Mobiles" --subdomain apex --owner-email owner@apex.example --owner-name "Anil Kumar" [--branch-code B01] [--branch-name "Main Branch"] [--gstin 27AAAAA0000A1Z5] [--timezone Asia/Kolkata] [--repairs true|false]`; the temporary Owner password is GENERATED (16 chars, `RandomNumberGenerator`) and printed ONCE to stdout with the login URL `https://<subdomain>.pos.storeos.io`.

Everything happens in ONE transaction, using the same domain factories as the application (`Tenant.Create`, `Branch.Create`, `User.Create`, `Role`, `UserBranch`, `UserRoleAssignment`):
1. Validate: subdomain matches the tenant regex (P-102) and is unused; branch code matches `^[A-Z1-9][A-Z0-9]{0,2}$`; owner e-mail valid; GSTIN (if given) matches the 15-character GSTIN pattern.
2. Create the tenant (features json `{"repairs": <flag>}`), the default branch, and the FIVE standard roles with their permission sets exactly per Plan §6.2 (see `prompts/_shared/CONTRACTS.md` §5 for the matrix): Owner, RegionalManager, BranchManager, Cashier, Technician.
3. Create the Owner user, assign the Owner role TENANT-WIDE (`branch_id = NULL`) and add a `user_branches` row for the default branch (`is_default = true`).
4. Write `audit_log` `tenant.provisioned` (tenant id, subdomain, operator = OS user name, correlation id = new GUID).
5. Idempotent re-run: if the subdomain already exists print an error and exit code 2 without changing anything.
Also refactor `tools/seed` (P-1201) to call the SAME provisioning code (`ProvisioningService` in `tools/provision` shared via a small class library `tools/StoreOS.Provisioning`) instead of duplicating role creation.

## 4. TASK

1. Create `tools/StoreOS.Provisioning` (class library: `ProvisioningService`, `StandardRoles`, `ProvisionRequest`, `ProvisionResult`) and `tools/provision` (console). Add both to `StoreOS.sln`.
2. Make `tools/seed` use `ProvisioningService` for the tenant/branch/roles/Owner part.
3. Tests in `StoreOS.IntegrationTests` (`ProvisioningTests`, `[Collection("postgres")]`): (a) provisioning creates exactly 1 tenant, 1 branch, 5 roles, 1 user, and the Owner can log in through the real login endpoint with the printed password and sees all permissions; (b) a second run with the same subdomain fails with exit code 2 and creates nothing; (c) invalid subdomain / branch code are rejected before any write; (d) two tenants provisioned back to back cannot see each other's roles through the APP role connection.
4. Document usage in `docs/RUNBOOK-provisioning.md` (including how to deactivate a tenant: `UPDATE tenants SET is_active = false` via the owner connection; the API then blocks login, P-303).

## 5. HARD CONSTRAINTS

- Provisioning uses the owner connection only; never add it to the API image or expose it as an HTTP endpoint (SaaS self-signup is out of scope, Plan §20).
- The generated password is shown once and never written to a log file or the database in clear text.
- No demo data is created by `provision`; demo data stays in `tools/seed`.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter "FullyQualifiedName~ProvisioningTests"
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1206 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1206 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1206` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the CLI options, the `ProvisioningService` signature, and the exact permission set of each of the five standard roles.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1206: <one-line summary>"` only after verification is
   fully green.
