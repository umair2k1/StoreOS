# P-1201 — Seed script: demo tenant, 2 branches, users per role, catalog, stock, IMEIs

**Phase:** 12 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1201** of the StoreOS Cloud build ("Seed script: demo tenant, 2 branches, users per role, catalog, stock, IMEIs") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- The ENTIRE backend (Phases 0-9) and ENTIRE frontend (Phases 10-11) are
  complete, tested, and green. Every module described in the master prompt's Section 2 (demo
  tenant flow) has a working implementation.
- No seed script exists yet to populate a realistic demo tenant for manual QA, the E2E suite
  (P-1202), and the final completeness audit (P-1205).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `tools/seed` connects with the OWNER connection (`STOREOS_MIGRATIONS_CONNECTION`, default `Username=storeos`), not the app role, and creates the tenant/branch/roles/Owner through `ProvisioningService` (P-1206). Because it inserts rows for several tenants it must never be shipped in the API image. In CI it runs AFTER `deploy/db/apply-post-migrate.sh`.

Create a new console project `tools/seed`:
```bash
dotnet new console -n seed -o tools/seed -f net10.0
dotnet sln StoreOS.sln add tools/seed/seed.csproj
dotnet add tools/seed reference backend/src/StoreOS.Domain
dotnet add tools/seed reference backend/src/StoreOS.Infrastructure
dotnet add tools/seed reference backend/src/StoreOS.Application
```

`tools/seed/SeedDemoTenant.cs` must create, via DIRECT calls to the SAME domain factories and
`IInventoryStockService`/`ICustomerLedgerService`/etc. used throughout the real application
(never raw SQL INSERTs, so every domain invariant/validation is exercised even during seeding):

- **1 tenant** ("Apex Mobiles Demo"), **2 branches** (B01 "Main Branch", B02 "Mall Branch").
- **1 user per role** at minimum: an Owner (all-branches, all permissions via a seeded
  "Owner" role), a Branch Manager (B01-scoped, most permissions except settings/users/branches
  management), a Cashier (B01-scoped, pos:checkout + sales:discount:apply only), a Technician
  (B01-scoped, repairs:manage only) - each with a KNOWN, documented password (e.g.
  "Demo@12345" for all, since this is a demo/dev seed, never used in production) printed to
  console on completion for manual login.
- **Catalog**: at least 5 NewPhone, 5 UsedPhone (with is_margin_scheme enabled), 10 Accessory,
  3 RepairService products, spanning realistic names/prices/GST rates.
- **Stock**: branch_inventory rows at BOTH branches for every physical product, with realistic
  quantities including at least one item deliberately at/below the low-stock threshold and one
  at exactly zero (out of stock), to exercise dashboard alerts.
- **IMEIs**: for the phone products, create InStock IMEI records (at least 3 per phone product
  per branch) via real purchase records (Purchase.Create + IInventoryStockService, matching
  the real stock-in flow).
- **A completed sales history**: several TaxInvoices spanning the last 7 days (to populate the
  dashboard's revenue chart), at least one Estimate, at least one credit sale with a customer
  and a partial subsequent payment, at least one Voided sale, at least one Refunded sale, at
  least one financed sale.
- **Customers**: at least 5, one with a credit_limit set and an existing balance near that
  limit (to exercise the credit-limit warning), one anonymized (to prove that flow works too).
- **Suppliers**: at least 3, with transactions establishing a realistic payable balance, and at
  least one supplier return.
- **Expenses**: several across different categories/months.
- **Liabilities**: at least one, with a partial repayment recorded.
- **Full repair job lifecycle**: at least one job in EACH status (Received through Delivered),
  including one with parts consumed and one fully billed (linked to a Sale via
  IRepairBillingService's two-step resolution).

## 4. TASK

1. Scaffold `tools/seed` exactly as shown.
2. Implement `SeedDemoTenant.cs` creating every item listed, using ONLY the real domain
   factories and application services (never raw SQL) so every invariant is genuinely exercised.
3. Make it idempotent-ish for convenience: check if a tenant named "Apex Mobiles Demo" already
   exists and, if so, either skip (printing a message) or accept a `--force` flag to wipe and
   re-seed (reusing the SAME delete-then-recreate pattern established in P-902, scoped to just
   this tenant).
4. Print a clear summary on completion: tenant id, all 4 users' emails/passwords/roles, branch
   ids/codes, and key entity counts.
5. Write `tools/seed.Tests` (new xunit project, added to the solution): assert that after
   running the seed, direct database queries confirm every listed category of data exists with
   the expected minimum counts (e.g. `>= 5` NewPhone products, `>= 1` job per repair status,
   etc.).

## 5. HARD CONSTRAINTS

- The seed script uses ONLY real domain factories/application services - never
  raw SQL inserts - so it doubles as a genuine end-to-end smoke exercise of the whole domain
  model's validation rules.
- The seeded password must be clearly documented as DEMO-ONLY and never suggested for any real
  production use.

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
dotnet run --project tools/seed -- --tenant-name "Apex Mobiles Demo"
dotnet test tools/seed.Tests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1201 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1201 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1201` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `tools/seed` project structure, the exact seeded user
emails/roles/branches, and the complete inventory of seeded entity categories with their
minimum counts.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1201: <one-line summary>"` only after verification is
   fully green.
