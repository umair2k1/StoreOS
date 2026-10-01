# P-110 — RLS branch-scoped policy archetype + retrofit to sales-shaped test table

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-110** of the StoreOS Cloud build ("RLS branch-scoped policy archetype + retrofit to sales-shaped test table") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-109 complete: every request runs inside an explicit transaction with
  `SET LOCAL app.tenant_id/app.branch_ids/app.all_branches` applied via the EF interceptor,
  proven end-to-end for tenant-only tables.
- No branch-scoped RLS policy (Plan §9.2 Archetype B) has been created yet, because no
  branch-scoped operational table (sales, inventory, etc.) exists until Phase 4/5.
- To prove the archetype now (rather than deferring proof to Phase 5, which would leave a
  long window of unverified risk), this prompt creates a minimal throwaway
  `rls_probe_branch_scoped` table with the same shape as a real branch-scoped table
  (`tenant_id`, `branch_id`, plus a `label` column), applies the Archetype B policy to it,
  proves it, then DROPS it in the same migration's sibling migration — no, instead: keep the
  table permanently as a living regression fixture used by P-111's CI schema test AND by
  every later branch-scoped-table prompt as a copy-paste reference; do not drop it.

## 3. CONTEXT BLOCK

Exact pattern (Plan §9.2, Policy Archetype B):

```sql
CREATE TABLE rls_probe_branch_scoped (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  label text NOT NULL
);
ALTER TABLE rls_probe_branch_scoped ENABLE ROW LEVEL SECURITY;
ALTER TABLE rls_probe_branch_scoped FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_branch_isolation_policy ON rls_probe_branch_scoped USING (
  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
  AND (
    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
  )
);
```

This table and policy are a **permanent regression fixture** — every future branch-scoped
entity (`sales`, `branch_inventory`, `purchases`, `inventory_imeis`,
`inventory_movements`, `customer_payments`, `customer_ledger_entries`,
`supplier_ledger_entries`, `expenses`, `repair_jobs`, etc.) copies this exact `USING` clause
verbatim, substituting only the table name. Document this fact prominently in
`docs/ARTIFACTS.md` under `## RLS Archetype B (copy verbatim for every branch-scoped table)`.

Edge case to test explicitly: `string_to_array('', ',')::uuid[]` on an EMPTY branch_ids CSV
(a user with zero specific branch assignments and `all_branches=false`) must not throw a cast
error — verify empirically; if Postgres raises `invalid input syntax for type uuid: ""`
on an empty string, the interceptor from P-109 must guard against emitting an empty CSV by
instead emitting a CSV containing the impossible sentinel `00000000-0000-0000-0000-000000000000`
when `BranchIds` is empty AND `AllBranches` is false — update
`RlsSessionInterceptor.ApplySessionSettingsAsync` from P-109 to do this:
`var branchIdsCsv = _scope.BranchIds.Count > 0 ? string.Join(",", _scope.BranchIds) :
"00000000-0000-0000-0000-000000000000";` — this guarantees the array cast always succeeds
and a user with no branches simply matches no real branch_id (the sentinel Guid is never a
real branch).

## 4. TASK

1. Generate migration `006_RlsBranchScopedPolicies` creating `rls_probe_branch_scoped`
   and its policy exactly as shown, via raw SQL in `Up()`, with a matching `DROP TABLE`/
   `DROP POLICY` in `Down()`.
2. Patch `RlsSessionInterceptor` (from P-109) with the empty-branch-list sentinel guard shown
   above.
3. Apply the migration.
4. Write `StoreOS.IntegrationTests/Security/RlsBranchScopedPolicyTests.cs` covering: (a) user
   with `all_branches=true` sees rows from any branch; (b) user scoped to branch X only sees
   branch X's rows, not branch Y's; (c) user with an EMPTY branch list and `all_branches=false`
   sees zero rows (proving the sentinel guard works, not a crash); (d) cross-tenant isolation
   still holds even when branch_id happens to collide numerically across tenants (use real
   distinct GUIDs, this is just a sanity re-check of the AND condition).
5. Append the "RLS Archetype B" block to `docs/ARTIFACTS.md` verbatim.

## 5. HARD CONSTRAINTS

- Do NOT drop `rls_probe_branch_scoped` — it stays permanently as a
  regression fixture referenced by P-111's CI schema test.
- The empty-branch-list sentinel value must be exactly
  `00000000-0000-0000-0000-000000000000` (the nil GUID) — later prompts and tests may assume
  this exact value.

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
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsBranchScopedPolicyTests
# Expected: all 4 cases pass, including the empty-branch-list zero-rows case (no exception).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-110 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-110 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-110` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact Archetype B SQL `USING` clause (for copy-paste into every later
branch-scoped table's RLS policy), and the nil-GUID sentinel convention for empty branch
lists.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-110: <one-line summary>"` only after verification is
   fully green.
