# P-516 — Finance provider list (settings-backed, normalize/dedupe, add/remove w/o touching history)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-516** of the StoreOS Cloud build ("Finance provider list (settings-backed, normalize/dedupe, add/remove w/o touching history)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-515 complete: checkout correctly composes and records finance details.
- The `settings` table (P-103) has a reserved key `"finance_providers"` (tenant-level JSON
  array) documented but never implemented. This prompt builds the CRUD for that list.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Settings/FinanceProviders/FinanceProviderDto.cs`:

```csharp
public record FinanceProviderDto(string NormalizedName, string DisplayName);
```

Create `GetFinanceProviders/GetFinanceProvidersQuery.cs` (`IRequest<IReadOnlyList<FinanceProviderDto>>`,
no special permission — any POS user needs this list for the finance dialog) reading the
`"finance_providers"` tenant-level setting row, deserializing its JSON array (empty list if the
setting doesn't exist yet).

Create `AddFinanceProvider/AddFinanceProviderCommand.cs` (`IRequiresPermission`,
`settings:manage`): takes a `DisplayName` string. Handler: loads (or creates if absent) the
`"finance_providers"` setting row; normalizes the new name
(`displayName.Trim()`, comparison key = `displayName.Trim().ToLowerInvariant()`); checks
case-insensitive duplicate against EXISTING normalized keys in the list — if a duplicate
exists, this is a no-op (idempotent add, return success without creating a second entry, do NOT
throw an error for "already exists" since Requirements §10.3 just says "deduplicate", not
"reject duplicates with an error"); otherwise appends `{normalizedName, displayName}` to the
JSON array and saves.

Create `RemoveFinanceProvider/RemoveFinanceProviderCommand.cs` (`IRequiresPermission`,
`settings:manage`): takes a `NormalizedName` string, removes the matching entry (by normalized
key) from the JSON array setting. CRITICALLY: removing a provider from this SUGGESTION LIST
must NEVER touch any existing `sales.finance_provider` string value on historical sales — those
are plain snapshot strings on the `Sale` entity already (per P-502's `FinanceProvider` column),
completely independent of this settings-backed list. Add an explicit test proving this.

Create `backend/src/StoreOS.Api/Controllers/SettingsController.cs` (if not already created by
an earlier prompt — check `docs/ARTIFACTS.md`; if this is the first Settings-related
controller, create it fresh) with `GET /api/settings/finance-providers`,
`POST /api/settings/finance-providers`, `DELETE /api/settings/finance-providers/{normalizedName}`.

## 4. TASK

1. Create the three vertical slices (`GetFinanceProviders`, `AddFinanceProvider`,
   `RemoveFinanceProvider`) under `StoreOS.Application/Settings/FinanceProviders/` exactly as
   described, using `System.Text.Json` for the JSON array read/write against the `Setting`
   entity's `ValueJson` property (P-103).
2. Create/extend `SettingsController.cs` with the three routes.
3. Write `StoreOS.IntegrationTests/Settings/FinanceProvidersTests.cs`: (a) adding a NEW
   provider appears in subsequent `GET`; (b) adding a CASE-INSENSITIVE duplicate (e.g. "hdfc
   bank" after "HDFC Bank" already exists) does NOT create a second entry (list length
   unchanged, confirm via GET); (c) removing a provider removes it from the list; (d) **the
   critical historical-preservation test**: create a checkout with `Finance.FinanceProvider =
   "HDFC Bank"` (P-515's flow), THEN remove "HDFC Bank" from the settings-backed suggestion
   list, THEN re-fetch the ORIGINAL sale via a direct DB query and confirm
   `sales.finance_provider` STILL reads `"HDFC Bank"` unchanged (Requirements §10.3: "Never
   modify historical sales when a provider is removed").

## 5. HARD CONSTRAINTS

- Deduplication is CASE-INSENSITIVE on the normalized key, but the
  ORIGINAL display casing of the FIRST-added entry is preserved and shown in `GET` responses
  (do not silently re-lowercase everything for display).
- Removing a provider from this list is PURELY a suggestion-list operation — it has zero
  effect on any historical `sales.finance_provider`/`sales.financer_name` values, which are
  independent snapshot strings, not foreign keys into this settings list.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter FinanceProvidersTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-516 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-516 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-516` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the three finance-provider use-case shapes, the `SettingsController` routes,
and confirmation that historical sales are provably unaffected by provider removal.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-516: <one-line summary>"` only after verification is
   fully green.
