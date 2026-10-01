# P-1121 — Admin screen: branches, users, roles, permission matrix

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1121** of the StoreOS Cloud build ("Admin screen: branches, users, roles, permission matrix") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1120 complete: settings screen works.
- GET/POST/PATCH /api/branches (P-310), /api/users, /api/roles (P-311) all exist. The 17-code
  PermissionCatalog (P-115) is the fixed universe of assignable permissions.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** add a 'Reset password' row action in the users table calling `POST /api/users/{id}/reset-password` (P-313); the dialog takes a temporary password and shows the same field errors as create-user.

Create `frontend/src/app/features/admin/branches-admin.component.ts` (CRUD list),
`frontend/src/app/features/admin/users-admin.component.ts` (list, create with initial
role+branch assignment, deactivate action), and
`frontend/src/app/features/admin/roles-admin.component.ts` with a PERMISSION MATRIX UI: a grid
of the 17 fixed permission codes (hardcode this list in the frontend too, mirroring
PermissionCatalog exactly - keep the two lists synchronized; add a comment noting they must
match) as rows, with a checkbox per role being created/edited, submitting the selected codes to
`POST /api/roles`.

## 4. TASK

1. Create all three admin components exactly as described.
2. Hardcode the 17 permission codes in a small `frontend/src/app/core/permissions/permission-catalog.ts`
   constant, mirroring the backend's `PermissionCatalog` (P-115) exactly, with a code comment
   noting the two lists must stay synchronized.
3. Write `.spec.ts` for all three: branch/user CRUD works; the role permission-matrix correctly
   submits the selected subset of codes; deactivating a user calls the correct endpoint and
   reflects the new state in the list.

## 5. HARD CONSTRAINTS

- The frontend's hardcoded permission-code list must exactly match the backend's
  `PermissionCatalog` (all 17 codes, exact strings) - mismatches would let an admin attempt to
  assign a code the backend rejects, or fail to expose a valid one.

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
cd store-os-cloud/frontend
ng test --include='**/branches-admin.component.spec.ts' --include='**/users-admin.component.spec.ts' --include='**/roles-admin.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1121 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1121 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1121` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: all three admin component structures and the frontend's permission-code constant list
(all 17, matching the backend exactly).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1121: <one-line summary>"` only after verification is
   fully green.
