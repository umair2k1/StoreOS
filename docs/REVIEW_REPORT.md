# Review report — storeos-prompts.zip (v1 -> v2)

Reviewed against `StoreOS-Complete-Product-Requirements.md` and `StoreOS-Cloud-Implementation-Plan.md`.
Result: 157 prompts (155 + 2 new). All 155 original prompts got the updated global-invariants block; 51 carry a substantive
`REVISION NOTE (review v2)`; 3 were rewritten (P-109, P-117, P-511). `python3 tools/verify_prompts.py` passes (and was mutation-tested to make sure it fails on broken input).

## How far the review went (read this so you know what it does NOT prove)
* Read in full or in large part: INDEX, MANIFEST, P-001, P-002, P-004, P-101, P-102, P-106, P-108, P-109, P-111, P-113, P-114, P-117, P-119, P-202, P-302-P-306, P-501, P-511, P-517, P-805, P-901, P-1008, P-1009, P-1201, P-1203.
* All 157 prompts were scanned mechanically (anatomy, headers, INDEX/MANIFEST agreement, forward references, C#/SQL code blocks for layering, stale designs, RLS expressions, package usage).
  The other prompts (most of Phase 4-9 CRUD/report prompts and the Angular Phase 10-11 prompts) were NOT read line by line; they only received the cross-cutting fixes and notes below.
* Nothing was compiled or run: no .NET SDK or package feed was available to the review. The C# in the rewritten prompts was written carefully but is unverified; the prompts' own tests are what will confirm it.
* The PostgreSQL behaviours relied on are standard (RLS never applies to superusers/BYPASSRLS; `set_config` empty-string after a transaction; isolation level fixed after the first query;
  40001 for a conflicting committed row under RepeatableRead/Serializable). The concurrency tests in P-117 / P-511 exist to prove them on your machine.

## A. Would have stopped the build or made it silently insecure/incorrect (fixed)
1. **Transaction + retry design impossible** (P-109, P-511): retry ran inside an already-aborted transaction; isolation escalation via `SET LOCAL` after `set_config` is rejected by PostgreSQL; commit happened after the response was written; refresh-token theft revocation was rolled back by the 401. -> D-01, rewritten P-109/P-511.
2. **Idempotency crashed under the exact case it exists for** (P-117): concurrent duplicate requests hit a primary-key violation (500). -> D-02, `IdempotencyBehavior` inside the transaction.
3. **RLS would not have been enforced at all**: compose/Testcontainers users are superusers, which bypass RLS even with FORCE. Tests would also have passed for the wrong reason. -> D-03, `storeos_app` role, `RlsRoleGuard`, fixture with owner/app connections, `post-migrate.sql` (also implements the plan's "no UPDATE/DELETE on ledgers").
4. **Solution would not compile**: 14 prompts placed Application-layer code that used Infrastructure types (circular reference). -> D-10, `IAppDbContext` + service interfaces + architecture test.
5. **Login, refresh and logout returned no rows** because RLS applies before authentication. -> D-09, anonymous tenant scope, tenant-prefixed refresh token, `ITenantResolver`.
6. **RLS policy error after the first request on a pooled connection** (`''::uuid`). -> `NULLIF(...)` in all policies + CI assertion + pool-bleed test.
7. **Background jobs (nightly reconciliation) would see zero rows** under FORCE RLS and report "all good". -> D-14, `ITenantJobRunner`.
8. **Branch code up to 10 chars vs 16-char invoice limit**: first checkout would throw. -> D-06.
9. **Estimates consumed the tax-invoice number series** (gaps in a "consecutive" series). -> D-07.
10. **Package allowlist / tooling gaps** that stop executors at P-004/P-101/P-1001/P-1203: Swashbuckle missing, EF reference for Application, no `FrameworkReference`, no local `dotnet-ef`, CI e2e job started Playwright before the API and migrated with the wrong role. -> D-15.
11. **Placeholders the pack itself forbids**: P-1008 tenant resolution always null (and anonymous `?tenantId=` override); P-1203 `services: docker: {}` placeholder. -> fixed.

## B. Missing pieces (added)
* P-313 change/reset password (there was no way to change a password).
* P-1206 tenant provisioning CLI (no way to create a real tenant/Owner/roles; seed was demo-only).
* Logo persistence + `GET /api/tenant/logo` (manifest, login and PDFs referenced a route that did not exist); PNG icons 192/512.
* Concrete CSV format for the inventory and GSTR-1 exports (the requirements say "spreadsheet"; the prompts only defined DTOs).
* Central `ErrorCatalog` (401/403/404/409 instead of 422 for auth failures, which would have broken the frontend's refresh-on-401).
* `IObjectStorageService` moved to Application with `GetAsync`; buckets ensured on startup.
* Architecture tests (layering, `*Query` markers, no stray `BeginTransaction`), `tools/verify_prompts.py`, `_shared/CONTRACTS.md`, `_shared/DECISIONS.md`.

## C. Consistency fixes
* 20 prompts had a reasoning level in the header that differed from MANIFEST; the higher level was kept in both.
* P-001's BLOCKED-line rule contradicted itself ("top of file" vs "below the header"); normalised across all prompts.
* `DomainException` created in Domain from the start; the P-202 "move" removed. P-112/P-113 run before P-109 (INDEX reordered).
* MANIFEST/INDEX regenerated: 157 rows, renamed files, new dependencies.

## D. Not changed, needs a human (see `prompts/_shared/DECISIONS.md`)
* **O-03 toolchain support dates** — .NET 8 ends 10 Nov 2026, Angular 18 and Node 20 are already out of support. Decide before P-001 (`TOOLCHAIN_UPGRADE.md`).
* O-01 invoice series (CA sign-off), O-02 GSTR-1 table mapping (CA review).
* Package versions in P-004/P-007 were not verified against a feed (`OpenTelemetry.Instrumentation.EntityFrameworkCore 1.9.0-beta.1` and `SixLabors.ImageSharp 3.1.5` are the two I would check first; ImageSharp has had security advisories).
* Default role permission sets (D-11) are my conservative choice where the plan says "configurable".

## E. Suggested first hour
1. Decide O-03. 2. `python3 tools/verify_prompts.py`. 3. Run P-001..P-007 and check `docs/BUILD_LOG.md`. 4. At P-108, confirm the role-guard and pool-bleed tests pass — they prove RLS is real.
