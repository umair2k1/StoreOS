# StoreOS Cloud — Implementation Prompt Manifest

Source of truth: `StoreOS-Cloud-Implementation-Plan.md` (architecture) +
`StoreOS-Complete-Product-Requirements.md` (behavior). Namespace convention:
**`StoreOS.*` everywhere** (the plan's stray `PhoneZone.*` references are normalized to
`StoreOS.*` in every prompt below — this is a deliberate correction, noted once here).

Conflict notes (Implementation Plan wins on architecture, Requirements wins on behavior):
- Plan §5 uses `PhoneZone.Domain.Tax` / `PhoneZone.Application` in two code samples →
  normalized to `StoreOS.Domain.Tax`, `StoreOS.Application.*` throughout.
- Plan's `products.purchase_price` default vs. Requirements' "generic catalog-wide default"
  language → resolved identically: both treat `products.purchase_price` as the last-resort
  non-IMEI cost basis, never used for margin scheme if `branch_inventory.cost_price > 0`.
- Requirements §8.2 gives two invoice formats without mandating one; Plan §8.5 makes the
  FY-consecutive series the recommended default with the daily format as a configurable
  legacy option. Implementation follows the Plan: `invoice_series_format` setting, FY series
  is the seed default.
- Everything else is consistent between the two documents.

Executor reasoning tags (a prompt header and this table always carry the same value; where they used to disagree the HIGHER one was kept): **low** = boilerplate/config/copy-exact-code, **med** = standard
CRUD, **high** = transactional/security-critical logic requiring careful sequencing.

---

## MANIFEST TABLE

| ID | Phase | Title | Files created/modified | Depends on | Reasoning | Verification |
|----|-------|-------|------------------------|------------|-----------|---------------|
| P-001 | 0 | Repo bootstrap, solution skeleton, docs ledger | `store-os-cloud/*.sln`, `backend/src/*/*.csproj`, `docs/BUILD_LOG.md`, `docs/ARTIFACTS.md`, `.gitignore` | — | low | `dotnet build` → 0 errors |
| P-002 | 0 | Docker dev stack: postgres, minio, otel-collector | `deploy/docker-compose.yml`, `deploy/otel-collector-config.yaml`, `deploy/db/init/01-app-role.sh`, `deploy/db/post-migrate.sql`, `deploy/db/apply-post-migrate.sh` | P-001 | low | `docker-compose up -d && docker-compose ps` all healthy |
| P-003 | 0 | Dockerfile.api, Dockerfile.web multi-stage | `deploy/Dockerfile.api`, `deploy/Dockerfile.web` | P-001 | low | `docker build` succeeds both |
| P-004 | 0 | NuGet package pinning + central package management | `backend/Directory.Packages.props`, `backend/Directory.Build.props` | P-001 | low | `dotnet restore` succeeds |
| P-005 | 0 | GitHub Actions CI skeleton (build+test only) | `.github/workflows/ci.yml` | P-001, P-004 | low | workflow YAML lint passes (`actionlint`) |
| P-006 | 0 | Angular workspace bootstrap (PWA schematics) | `frontend/*` (ng new output), `frontend/angular.json` | — | low | `ng build` → 0 errors |
| P-007 | 0 | Angular npm package pinning | `frontend/package.json` exact versions | P-006 | low | `npm ci` succeeds |
| P-101 | 1 | Core EF DbContext skeleton + Npgsql registration | `StoreOsDbContext.cs`, `IAppDbContext.cs`, `StoreOsDbContextFactory.cs`, `appsettings.json` (app-role connection strings) | P-001, P-004 | med | `dotnet build` |
| P-102 | 1 | Tenants + Branches entities/migration | `StoreOS.Domain/Tenancy/*.cs`, migration `001_InitTenancyBranches` | P-101 | med | `dotnet ef database update` |
| P-103 | 1 | Settings key/value entity/migration | `StoreOS.Domain/Tenancy/Setting.cs`, migration `002_Settings` | P-102 | low | migration applies |
| P-104 | 1 | Users, Roles, RolePermissions, UserBranches, UserRoleAssignments entities/migration | `StoreOS.Domain/Identity/*.cs`, migration `003_Identity` | P-102 | med | migration applies |
| P-105 | 1 | RefreshTokens entity/migration | `StoreOS.Domain/Identity/RefreshToken.cs`, migration `004_RefreshTokens` | P-104 | low | migration applies |
| P-106 | 1 | IAccessScope + AccessScopeMiddleware (tenant/branch resolution from JWT) | `StoreOS.Api/Middleware/AccessScopeMiddleware.cs`, `StoreOS.Application/Abstractions/IAccessScope.cs` | P-105 | high | integration test: scope populated from claims |
| P-107 | 1 | EF Core global query filters (tenant+branch) wiring | `StoreOsDbContext` OnModelCreating filters | P-106 | high | integration test: cross-tenant query returns 0 rows |
| P-108 | 1 | RLS SQL migration: enable+force RLS, tenant-only policy archetype | migration `005_RlsTenantOnlyPolicies`, `PostgresTestFixture.cs`, `RlsRoleGuard.cs` | P-102–105 | high | `\d+ customers` shows rls enabled (once table exists; placeholder table `rls_probe`) |
| P-109 | 1 | TransactionBehavior — one transaction per request, RLS session variables, tenant job runner | `StoreOS.Application/Abstractions/IRequestMarkers.cs`, `ITenantJobRunner.cs`, `StoreOS.Infrastructure/Persistence/RlsSessionInterceptor.cs`, `TransactionBehavior.cs`, `TenantJobRunner.cs` | P-107, P-108, P-112, P-113 | high | integration test: 8 groups (scoped reads, rollback, read-only, pool bleed, nested join, anonymous flow, job runner, isolation levels) |
| P-110 | 1 | RLS branch-scoped policy archetype + retrofit to sales-shaped test table | migration `006_RlsBranchScopedPolicies` | P-109 | high | integration test proves branch isolation |
| P-111 | 1 | CI schema test: 100% RLS coverage assertion | `StoreOS.IntegrationTests/Schema/RlsCoverageTests.cs` | P-108, P-110 | high | `dotnet test --filter RlsCoverageTests` green |
| P-112 | 1 | CorrelationIdMiddleware + Serilog enrichment | `StoreOS.Api/Middleware/CorrelationIdMiddleware.cs`, `Program.cs` Serilog config | P-101 | med | integration test: response echoes `X-Correlation-ID` |
| P-113 | 1 | ProblemDetails/DomainException/error pipeline | `StoreOS.Domain/Exceptions/DomainException.cs`, `StoreOS.Api/Middleware/ExceptionHandlingMiddleware.cs`, `ErrorCatalog.cs` | P-112 | med | integration test: DomainException → 422 with code+correlationId; catalog maps 401/403/404/409 |
| P-114 | 1 | FluentValidation + MediatR pipeline behaviors (validation, authorization stubs-forbidden real impl) | `StoreOS.Application/Behaviors/ValidationBehavior.cs`, `AuthorizationBehavior.cs`, `IntegrationTests/ArchitectureTests.cs` | P-104, P-113 | high | unit test: invalid command → 400 |
| P-115 | 1 | Permission catalog seed + IPermissionChecker | `StoreOS.Domain/Identity/PermissionCatalog.cs`, `StoreOS.Application/Identity/IPermissionChecker.cs` | P-114 | med | unit test: catalog contains all §6.2 permission codes |
| P-116 | 1 | audit_log table/migration + IAuditWriter service | `StoreOS.Domain/Platform/AuditLogEntry.cs`, migration `007_AuditLog`, `IAuditWriter.cs` | P-109, P-112 | med | integration test: write inserts row with correlation_id |
| P-117 | 1 | idempotency_keys table/migration + IdempotencyBehavior (in-transaction idempotency) | migration `008_Idempotency`, `StoreOS.Application/Behaviors/IdempotencyBehavior.cs`, `StoreOS.Infrastructure/Persistence/IdempotencyStore.cs` | P-109, P-114 | high | integration test: replay, key reuse → 409, rolled-back request leaves no record |
| P-118 | 1 | Money/decimal conventions + Money.Round2 utility + global analyzer ban on double/float in Domain | `StoreOS.Domain/Common/Money.cs`, `.editorconfig` banned-symbols | P-101 | low | unit test: Round2 half-up cases |
| P-119 | 1 | ReportingDbContext seam + connection string wiring | `StoreOS.Infrastructure/Persistence/ReportingDbContext.cs`, `appsettings.json` `ConnectionStrings:Reporting` | P-101 | med | integration test: ReportingDbContext reads seeded row via separate connection |
| P-201 | 2 | TaxEngine: StandardGst | `StoreOS.Domain/Tax/TaxEngine.cs`, `StoreOS.Domain.Tests/Tax/StandardGstTests.cs` | P-118 | high | `dotnet test` listed cases pass |
| P-202 | 2 | TaxEngine: MarginSchemeGst + cost-guard | `StoreOS.Domain/Tax/TaxEngine.cs` (extend), `MarginSchemeGstTests.cs` | P-201 | high | tests incl. cost<=0 → DomainValidationException |
| P-203 | 2 | TaxEngine: proportional discount factor | `StoreOS.Domain/Tax/DiscountEngine.cs`, `DiscountEngineTests.cs` | P-201 | med | tests incl. discount=subtotal, discount=0 |
| P-204 | 2 | FinanceEngine: EMI with DecimalPow | `StoreOS.Domain/Finance/FinanceEngine.cs`, `FinanceEngineTests.cs` | P-118 | high | tests: zero interest, positive interest, invalid tenure, zero principal, downpayment>total |
| P-205 | 2 | InventoryValuation pure calculator | `StoreOS.Domain/Inventory/ValuationCalculator.cs`, tests | P-118 | med | tests: cost/retail/potential valuation, excludes RepairService |
| P-206 | 2 | Invoice number formatter (both series formats, pure) | `StoreOS.Domain/Sales/InvoiceNumberFormatter.cs`, tests | P-118 | med | tests: FY format ≤16 chars, daily format shape |
| P-301 | 3 | User entity password hashing (Argon2/PBKDF2) service | `StoreOS.Infrastructure/Identity/PasswordHasher.cs` | P-104 | med | unit test: hash/verify roundtrip, never plaintext stored |
| P-302 | 3 | JWT issuance service (access token, 15 min, claims: tenant/branches/permissions/token_version) | `StoreOS.Infrastructure/Identity/JwtTokenService.cs` | P-115, P-301 | high | unit test: decoded claims match expected shape |
| P-303 | 3 | POST /auth/login endpoint + handler | `StoreOS.Application/Identity/Login/*.cs`, `AuthController.cs` | P-302 | high | integration test: valid creds → 200 + access token; invalid → 401 |
| P-304 | 3 | Refresh token issuance + rotation on login | `StoreOS.Infrastructure/Identity/RefreshTokenService.cs` | P-105, P-303 | high | integration test: login sets HttpOnly cookie |
| P-305 | 3 | POST /auth/refresh with family reuse detection | `StoreOS.Application/Identity/Refresh/*.cs` | P-304 | high | integration test: replay of rotated token revokes family |
| P-306 | 3 | POST /auth/logout + GET /auth/me | `StoreOS.Application/Identity/Logout/*.cs`, `GetMe/*.cs` | P-305 | med | integration test both endpoints |
| P-307 | 3 | token_version stale-JWT invalidation check (30s cache) | `StoreOS.Application/Behaviors/TokenVersionBehavior.cs` | P-306 | high | integration test: bump token_version → old JWT rejected |
| P-308 | 3 | High-impact operation live-DB permission re-check behavior | `StoreOS.Application/Behaviors/LiveDbPermissionBehavior.cs` | P-307, P-115 | high | integration test: revoke sales:void mid-session → immediate 403 |
| P-309 | 3 | X-Branch-Id middleware + branch access enforcement | `StoreOS.Api/Middleware/BranchContextMiddleware.cs` | P-106 | high | integration test: user without branch access on header → 403 |
| P-310 | 3 | Branches CRUD endpoints | `StoreOS.Application/Tenancy/Branches/*.cs`, `BranchesController.cs` | P-309 | med | integration CRUD test |
| P-311 | 3 | Users/roles/branch-assignment admin endpoints | `StoreOS.Application/Identity/Users/*.cs`, `UsersController.cs` | P-310 | med | integration CRUD test |
| P-312 | 3 | Rate limiting (login 5/min/IP, global 200/min/user) | `StoreOS.Api/RateLimiting/*.cs` | P-303 | low | integration test: 6th login attempt in window → 429 |
| P-313 | 3 | Change own password + admin password reset | `StoreOS.Application/Identity/Password/*`, routes `POST /api/auth/change-password`, `POST /api/users/{id}/reset-password` | P-301, P-305, P-307, P-311, P-312 | med | integration test: sessions die on change/reset |
| P-401 | 4 | Products/catalog entity + migration | `StoreOS.Domain/Catalog/Product.cs`, migration `009_Products` | P-110 | med | migration applies + RLS policy added |
| P-402 | 4 | Products CRUD endpoints | `StoreOS.Application/Catalog/Products/*.cs`, `ProductsController.cs` | P-401 | med | integration CRUD, category enum validated |
| P-403 | 4 | branch_inventory entity + migration + RLS policy | `StoreOS.Domain/Inventory/BranchInventory.cs`, migration `010_BranchInventory` | P-401 | med | migration applies |
| P-404 | 4 | GET /inventory + low-stock + valuation endpoints | `StoreOS.Application/Inventory/Queries/*.cs`, `InventoryController.cs` | P-403, P-205 | med | integration test: low-stock threshold, valuation math |
| P-405 | 4 | inventory_movements append-only ledger entity+migration | `StoreOS.Domain/Inventory/InventoryMovement.cs`, migration `011_InventoryMovements` | P-403 | med | migration applies |
| P-406 | 4 | IInventoryStockService: conditional decrement/increment (internal app service contract) | `StoreOS.Application/Inventory/IInventoryStockService.cs` + impl | P-405 | high | integration test: concurrent decrement to zero, one fails |
| P-407 | 4 | inventory_imeis entity + migration + tenant-wide active-unique index | `StoreOS.Domain/Inventory/InventoryImei.cs`, migration `012_InventoryImeis` | P-403 | high | migration applies; duplicate active IMEI insert fails at DB level |
| P-408 | 4 | IInventoryStockService: SellImeisAsync / status transitions | extend `IInventoryStockService` impl | P-406, P-407 | high | integration test: sell→sold, duplicate sale rejected |
| P-409 | 4 | Purchases entity + migration | `StoreOS.Domain/Purchasing/Purchase.cs`, migration `013_Purchases` | P-403 | med | migration applies |
| P-410 | 4 | POST /purchases stock-in endpoint (with optional IMEIs) | `StoreOS.Application/Purchasing/StockIn/*.cs`, `PurchasesController.cs` | P-408, P-409 | high | integration test: stock-in increments qty + creates IMEI rows |
| P-411 | 4 | Suppliers minimal master (needed as FK for purchases) entity+migration | `StoreOS.Domain/Suppliers/Supplier.cs`, migration `014_SuppliersMaster` | P-401 | med | migration applies |
| P-412 | 4 | Supplier invoice import pipeline (deterministic, dedupe on import_batch) | `StoreOS.Application/Purchasing/ImportSupplierInvoice/*.cs`, `import_batches` migration `015` | P-410, P-411 | high | integration test: duplicate import batch skipped, ambiguous mapping → manual step response |
| P-413 | 4 | Inventory adjustments endpoint (live-permission gated) | `StoreOS.Application/Inventory/Adjustments/*.cs` | P-406, P-308 | med | integration test: adjustment writes movement + audit |
| P-414 | 4 | Branch transfer out/in endpoints | `StoreOS.Application/Inventory/Transfers/*.cs`, migration `016_Transfers` | P-408 | high | integration test: transfer preserves tenant-wide stock total, IMEI InTransit |
| P-501 | 5 | invoice_sequences table + upsert SQL helper | migration `017_InvoiceSequences`, `StoreOS.Infrastructure/Sales/InvoiceSequenceAllocator.cs` | P-206, P-403 | high | concurrency test: 50 parallel allocations → unique consecutive seqs |
| P-502 | 5 | sales + sale_items entities + migration | `StoreOS.Domain/Sales/Sale.cs`, `SaleItem.cs`, migration `018_Sales` | P-501 | high | migration applies |
| P-503 | 5 | GET /pos/search + GET /pos/product/{id} endpoints | `StoreOS.Application/Sales/Search/*.cs`, `PosController.cs` | P-403, P-407 | med | integration test: search returns branch-scoped stock + IMEIs |
| P-504 | 5 | CheckoutCommand + validator (cart shape, qty>0, payment shape) — validation-only, no persistence yet | `StoreOS.Application/Sales/Checkout/CheckoutCommand.cs`, `CheckoutValidator.cs` | P-114, P-502 | high | unit test: invalid cart rejected with field errors |
| P-505 | 5 | Server-side pricing resolution (branch override ?? product price) + margin scheme cost cascade wiring | `StoreOS.Application/Sales/Checkout/PricingResolver.cs` | P-202, P-403 | high | unit test: override resolution + IMEI cost cascade + unseeded cost → 422 |
| P-506 | 5 | customer_ledger_entries + customers minimal entity/migration (needed for checkout) | `StoreOS.Domain/Customers/Customer.cs`, `CustomerLedgerEntry.cs`, migration `019_CustomersLedger` | P-502 | high | migration applies |
| P-507 | 5 | ICustomerLedgerService (RecordInvoiceDebitAsync etc.) | `StoreOS.Application/Customers/ICustomerLedgerService.cs` + impl | P-506 | high | unit+integration test: debit/credit entries balance |
| P-508 | 5 | customer_payments entity/migration + payment composition in checkout | `StoreOS.Domain/Sales/CustomerPayment.cs`, migration `020_CustomerPayments` | P-506 | med | migration applies |
| P-509 | 5 | Credit limit check (tenant-global consolidated balance) | `StoreOS.Application/Sales/Checkout/CreditLimitChecker.cs` | P-507 | high | integration test: cross-branch debt sum blocks over-limit unless override permission |
| P-510 | 5 | CheckoutHandler full transaction assembly (steps 1–10 per plan §10.1) | `StoreOS.Application/Sales/Checkout/CheckoutHandler.cs` | P-408, P-501, P-505, P-507, P-508, P-509, P-116 | high | integration test: full checkout creates sale+items+movements+ledger+audit atomically |
| P-511 | 5 | SerializationRetryBehavior — retry the whole transaction on 40001/40P01; wire Idempotency-Key into checkout | `StoreOS.Infrastructure/Persistence/SerializationRetryBehavior.cs`, `CheckoutCommand` markers | P-109, P-117, P-510 | high | unit + contention tests: last unit sold once; same key in parallel → one sale |
| P-512 | 5 | Checkout concurrency test suite (last-unit race, IMEI double-sell, parallel invoice numbering) | `StoreOS.ConcurrencyTests/CheckoutConcurrencyTests.cs` | P-511 | high | `dotnet test StoreOS.ConcurrencyTests` green |
| P-513 | 5 | POST /pos/checkout controller wiring + response DTO | `PosController.cs` extend | P-511 | med | integration test: 201 with invoice number |
| P-514 | 5 | Gift line handling + invoice-level discount wiring into checkout | extend `CheckoutHandler`, `PricingResolver` | P-510, P-203 | med | integration test: gift line excluded from revenue calc, discount reduces GST proportionally |
| P-515 | 5 | Finance/EMI fields in checkout (finance provider, down payment, EMI calc, tenure) | extend `CheckoutHandler`; `StoreOS.Application/Sales/Checkout/FinanceComposer.cs` | P-510, P-204 | high | integration test: financed sale persists emi_details jsonb |
| P-516 | 5 | Finance provider list (settings-backed, normalize/dedupe, add/remove w/o touching history) | `StoreOS.Application/Settings/FinanceProviders/*.cs` | P-103 | med | integration test: case-insensitive dedupe, removal doesn't alter old sales |
| P-517 | 5 | Estimate invoice_type support in checkout (bypass stock/IMEI decrement + no ledger debit) | extend `CheckoutHandler` | P-510 | high | integration test: estimate creates 0 movements, 0 ledger entries |
| P-518 | 5 | POST /pos/estimate endpoint | `PosController.cs` extend, `StoreOS.Application/Sales/CreateEstimate/*.cs` | P-517 | med | integration test |
| P-519 | 5 | converted_from_estimate_id / linked_tax_invoice_id columns + migration | migration `021_EstimateConversion` | P-502 | low | migration applies |
| P-520 | 5 | POST /pos/estimate/{id}/convert handler (plan §10.7 steps 1–11) | `StoreOS.Application/Sales/ConvertEstimate/*.cs` | P-519, P-511, P-509 | high | integration test: convert allocates new invoice #, decrements stock, links bidirectionally |
| P-521 | 5 | GET /sales + filters/sort/pagination | `StoreOS.Application/Sales/Search/GetSales*.cs`, `SalesController.cs` | P-502 | high | integration test: every filter combination from Req §16.4 |
| P-522 | 5 | GET /sales/{id} detail endpoint | `SalesController.cs` extend | P-521 | med | integration test |
| P-523 | 5 | POST /sales/{id}/payments (subsequent payment, never mutate existing) | `StoreOS.Application/Sales/RecordPayment/*.cs` | P-508, P-117 | high | integration test: 2 partial payments = 2 rows, balance recalculated |
| P-524 | 5 | POST /sales/{id}/void (live-permission gated, reversing movements+ledger) | `StoreOS.Application/Sales/VoidSale/*.cs` | P-308, P-406, P-507 | high | integration test: void preserves original row, reverses stock+IMEI+ledger |
| P-525 | 5 | POST /sales/{id}/refund | `StoreOS.Application/Sales/RefundSale/*.cs` | P-524 | high | integration test: refund preserves sale, restores stock/IMEI |
| P-601 | 6 | credit_limit column + POS live balance display endpoint | migration `022_CreditLimit`, `StoreOS.Application/Customers/GetCreditBalance/*.cs` | P-509 | med | integration test |
| P-602 | 6 | Customers CRUD endpoints | `StoreOS.Application/Customers/Crud/*.cs`, `CustomersController.cs` | P-506 | med | integration CRUD |
| P-603 | 6 | GET /customers/{id}/ledger + /sales | `CustomersController.cs` extend | P-602 | med | integration test: per-invoice branch attribution shown |
| P-604 | 6 | POST /customers/{id}/anonymize (DPDP erasure) | `StoreOS.Application/Customers/Anonymize/*.cs` | P-602 | high | integration test: PII scrubbed, invoice numbers/amounts/GST intact |
| P-605 | 6 | supplier_transactions + supplier_ledger_entries entities/migration | migration `023_SupplierTransactions` | P-411 | med | migration applies |
| P-606 | 6 | Supplier transactions endpoints + payable derivation | `StoreOS.Application/Suppliers/Transactions/*.cs`, `SuppliersController.cs` | P-605 | med | integration test: payable = opening+bills-payments-credits |
| P-607 | 6 | supplier_returns + supplier_return_items entities/migration | migration `024_SupplierReturns` | P-605, P-407 | med | migration applies |
| P-608 | 6 | POST /supplier-returns handler (atomic: header+lines+stock+IMEI+ledger) | `StoreOS.Application/Suppliers/CreateReturn/*.cs` | P-607, P-408 | high | integration test: return decrements stock, IMEI→ReturnedToSupplier, ledger credit by settlement type |
| P-609 | 6 | expenses entity/migration + CRUD endpoints | `StoreOS.Domain/FinanceOps/Expense.cs`, migration `025_Expenses`, `ExpensesController.cs` | P-102 | med | integration CRUD + monthly total |
| P-610 | 6 | liabilities + liability_payments entities/migration | migration `026_Liabilities` | P-102 | med | migration applies |
| P-611 | 6 | Liabilities CRUD + repayment endpoint (balance = principal - Σrepayments) | `StoreOS.Application/FinanceOps/Liabilities/*.cs`, `LiabilitiesController.cs` | P-610 | med | integration test: repayment never rewrites principal |
| P-701 | 7 | repair_services entity/migration + CRUD (defaults SAC/GST/duration/warranty) | `StoreOS.Domain/Repairs/RepairService.cs`, migration `027_RepairServices` | P-401 | med | integration test: defaults applied |
| P-702 | 7 | repair_jobs entity/migration + job number sequence (REP-BRANCH-YYYYMMDD-XXXX) | `StoreOS.Domain/Repairs/RepairJob.cs`, migration `028_RepairJobs`, `RepairJobNumberAllocator.cs` | P-701 | high | concurrency test: parallel job creation same day → unique sequence |
| P-703 | 7 | Repair job CRUD + intake/diagnosis/commercial fields endpoints | `StoreOS.Application/Repairs/Jobs/*.cs`, `RepairJobsController.cs` | P-702 | med | integration CRUD |
| P-704 | 7 | repair_job_status_history entity/migration + POST /repair-jobs/{id}/status (state machine) | migration `029_RepairStatusHistory`, `StoreOS.Application/Repairs/ChangeStatus/*.cs` | P-703 | high | integration test: illegal transition rejected, history row per change |
| P-705 | 7 | repair_job_parts entity/migration + reserve/consume/return via IInventoryStockService | migration `030_RepairJobParts`, `StoreOS.Application/Repairs/Parts/*.cs` | P-704, P-406 | high | integration test: consume decrements stock atomically, negative stock forbidden |
| P-706 | 7 | Customer approval + warranty fields endpoints | `RepairJobsController.cs` extend | P-703 | med | integration test |
| P-707 | 7 | Repair billing: two-step FK resolution linking repair_job ↔ sale | `StoreOS.Application/Repairs/BillRepair/*.cs`, `IRepairBillingService.cs` | P-510, P-705 | high | integration test: no deadlock under two-step insert, bidirectional link verified |
| P-708 | 7 | Repair feature flag (IFeatureManager wrapper over tenants.features jsonb) | `StoreOS.Application/Platform/IFeatureManager.cs` | P-102 | high | integration test: disabling hides endpoints' effect but preserves history |
| P-801 | 8 | Dashboard aggregate query (today's revenue, invoice count, profit, active repairs, low-stock, top-5, 7-day chart) via ReportingDbContext | `StoreOS.Application/Reporting/Dashboard/*.cs`, `ReportsController.cs` | P-119, P-510, P-205 | high | integration test: each metric matches Req §5 exclusion rules exactly |
| P-802 | 8 | Daily Sales Audit report (sales summary, inventory summary, payment breakdown, category breakdown, itemized log) | `StoreOS.Application/Reporting/DailySales/*.cs` | P-801 | high | integration test: report totals reconcile with seeded transactions |
| P-803 | 8 | Multi-branch comparison report + consolidated totals | `StoreOS.Application/Reporting/Compare/*.cs` | P-802 | med | integration test: consolidated == Σ branch reports |
| P-804 | 8 | Inventory valuation/report export endpoint (multi-category, service-specific representation) | `StoreOS.Application/Reporting/InventoryExport/*.cs` | P-404 | med | integration test: repair services in separate representation |
| P-805 | 8 | GSTR-1 export grouped by GSTIN | `StoreOS.Application/Reporting/Gstr1Export/*.cs` | P-802 | high | integration test: only active tax invoices included, grouped correctly |
| P-806 | 8 | QuestPDF invoice/estimate template (DTO-only, IRN/QR slots reserved) | `StoreOS.Infrastructure/Printing/InvoicePdfRenderer.cs` | P-522 | med | unit test: renders without throwing, contains required fields |
| P-807 | 8 | QuestPDF repair ticket/invoice + supplier return note templates | `StoreOS.Infrastructure/Printing/RepairPdfRenderer.cs`, `SupplierReturnPdfRenderer.cs` | P-806 | med | unit test both render |
| P-808 | 8 | QuestPDF daily sales / inventory report templates | `StoreOS.Infrastructure/Printing/ReportPdfRenderer.cs` | P-806 | low | unit test renders |
| P-809 | 8 | GET /sales/{id}/invoice.pdf, /repair-jobs/{id}/ticket.pdf, /supplier-returns/{id}/note.pdf endpoints | Controllers extend | P-806, P-807 | med | integration test: 200 + application/pdf |
| P-901 | 9 | JSON backup export (tenant/export) — all tables per §28 | `StoreOS.Application/Platform/BackupExport/*.cs` | P-116, P-119 | high | integration test: exported JSON contains all required tables, schema version stamped |
| P-902 | 9 | JSON restore/import (validate, staged transaction, reject partial) | `StoreOS.Application/Platform/BackupImport/*.cs` | P-901 | high | integration test: invalid file rejected without touching DB; valid restores atomically |
| P-903 | 9 | Desktop migration tool `pz-migrate`: schema detect + dry-run + reconciliation report | `tools/pz-migrate/*.cs` (console) | P-902 | high | CLI test: dry-run produces reconciliation counts matching fixture |
| P-904 | 9 | pz-migrate record-level dedupe (sales/IMEIs/purchases/customers two-tier) + reconcile-customers CLI | `tools/pz-migrate/Dedup/*.cs` | P-903 | high | CLI test: re-run on updated fixture imports only new records |
| P-905 | 9 | Nightly reconciliation Hangfire job (5 sweeps) | `StoreOS.Infrastructure/Jobs/ReconciliationJob.cs` | P-119, P-406, P-507, P-606 | high | integration test: seeded drift triggers alert entry |
| P-906 | 9 | Hangfire wiring + correlation-id propagation into jobs | `StoreOS.Api/Jobs/HangfireSetup.cs` | P-905, P-112 | med | integration test: job log line carries correlation id |
| P-1001 | 10 | Angular core module: HTTP client, environment config, generated API client wiring (NSwag) | `frontend/src/app/core/*`, `nswag.json` | P-513 (any endpoint to generate from) | med | `ng build` + generated client compiles |
| P-1002 | 10 | AuthSignalStore (in-memory access token) + auth interceptor | `frontend/src/app/core/auth/*.ts` | P-1001, P-303 | high | unit test: interceptor attaches bearer, buffers on 401 |
| P-1003 | 10 | Silent refresh on APP_INITIALIZER + refresh flow | `frontend/src/app/core/auth/silent-refresh.ts` | P-1002, P-305 | high | unit test: bootstrap calls refresh once |
| P-1004 | 10 | Correlation-id + idempotency-key interceptors | `frontend/src/app/core/interceptors/*.ts` | P-1001 | med | unit test headers present |
| P-1005 | 10 | Route guards (auth guard, permission directive `*pzCan`) | `frontend/src/app/core/guards/*.ts`, `permission.directive.ts` | P-1002 | med | unit test: directive hides on missing permission |
| P-1006 | 10 | App shell: top nav, branch switcher, dynamic title/branding service | `frontend/src/app/shared/shell/*.ts` | P-1005, P-310 | high | unit test: title updates on branch/tenant signal change |
| P-1007 | 10 | Onboarding wizard (shop name + branding prompt on first run) | `frontend/src/app/features/onboarding/*.ts` | P-1006 | med | e2e-lite test: submits and redirects |
| P-1008 | 10 | Dynamic W3C manifest endpoint + Angular link wiring | `StoreOS.Api/Platform/ManifestController.cs`, `frontend/src/index.html` | P-1007 | med | integration test: manifest JSON matches tenant branding |
| P-1009 | 10 | Tenant logo upload (validation, ImageSharp resize) endpoint + Angular uploader | `StoreOS.Application/Platform/LogoUpload/*.cs`, `frontend/src/app/features/onboarding/logo-uploader.component.ts` | P-1008 | high | integration test: rejects bad mime/oversize/bad magic bytes |
| P-1010 | 10 | Login screen + lock screen components | `frontend/src/app/features/auth/*.ts` | P-1003 | med | unit test renders, submits |
| P-1011 | 10 | Shared design system: pz-table, pz-form-field, pz-dialog, money pipe, date pipe, empty/loading/error states, toast service | `frontend/src/app/shared/*` | P-1001 | med | unit tests each component |
| P-1101 | 11 | POS screen: search/scan/cart shell + hotkeys | `frontend/src/app/features/pos/*.ts` | P-1011, P-503 | high | unit test: barcode wedge timing logic |
| P-1102 | 11 | POS: customer selection + credit balance display + limit warning dialog | `frontend/src/app/features/pos/customer-select/*.ts` | P-1101, P-601 | high | unit test: warning shown when balance+new>limit |
| P-1103 | 11 | POS: cart editing (qty, IMEI entry, margin toggle, gift, discount) | `frontend/src/app/features/pos/cart/*.ts` | P-1101 | high | unit test each cart rule |
| P-1104 | 11 | POS: payment panel (cash/UPI/card/credit/split) + finance dialog | `frontend/src/app/features/pos/payment/*.ts` | P-1103, P-516 | high | unit test: EMI preview matches backend formula |
| P-1105 | 11 | POS: checkout submission + invoice print flow (server PDF, printing-never-rolls-back toast) | `frontend/src/app/features/pos/checkout.service.ts` | P-1104, P-513, P-806 | high | unit test: print failure still shows success toast w/ reprint action |
| P-1106 | 11 | POS: estimate save + convert-to-invoice action | `frontend/src/app/features/pos/estimate/*.ts` | P-1105, P-520 | med | unit test |
| P-1107 | 11 | Sales history screen: search/filters/sort/pagination | `frontend/src/app/features/sales-history/*.ts` | P-1011, P-521 | med | unit test filter wiring |
| P-1108 | 11 | Sales detail view + void/refund/record-payment actions | `frontend/src/app/features/sales-history/detail/*.ts` | P-1107, P-523, P-524, P-525 | med | unit test action gating by permission directive |
| P-1109 | 11 | Inventory list screen: search/filter/sort/paginate/bulk select | `frontend/src/app/features/inventory/*.ts` | P-1011, P-404 | med | unit test |
| P-1110 | 11 | Inventory item detail + stock-in form + IMEI capture | `frontend/src/app/features/inventory/detail/*.ts` | P-1109, P-410 | med | unit test |
| P-1111 | 11 | Supplier invoice import screen (upload, mapping confirmation) | `frontend/src/app/features/purchases/import/*.ts` | P-1110, P-412 | med | unit test: ambiguous mapping shows confirmation UI |
| P-1112 | 11 | Branch transfer screen | `frontend/src/app/features/transfers/*.ts` | P-1109, P-414 | med | unit test |
| P-1113 | 11 | Customers directory + detail (history, ledger, credit, repairs) | `frontend/src/app/features/customers/*.ts` | P-1011, P-602, P-603 | med | unit test |
| P-1114 | 11 | Suppliers directory + detail (transactions, payable, returns) | `frontend/src/app/features/suppliers/*.ts` | P-1011, P-606, P-608 | med | unit test |
| P-1115 | 11 | Expenses screen | `frontend/src/app/features/expenses/*.ts` | P-1011, P-609 | low | unit test |
| P-1116 | 11 | Liabilities screen | `frontend/src/app/features/liabilities/*.ts` | P-1011, P-611 | low | unit test |
| P-1117 | 11 | Repairs: service catalogue + job board (kanban by status) | `frontend/src/app/features/repairs/*.ts` | P-1011, P-703, P-704 | high | unit test: status transitions call correct endpoint |
| P-1118 | 11 | Repairs: job detail (intake, diagnosis, parts, approval, billing) | `frontend/src/app/features/repairs/detail/*.ts` | P-1117, P-705, P-707 | high | unit test |
| P-1119 | 11 | Reports screen: dashboard widgets, daily audit, comparison, exports | `frontend/src/app/features/reports/*.ts` | P-1011, P-801–805 | high | unit test each widget |
| P-1120 | 11 | Settings screen: shop info, tax, bank, finance providers, theme, repair flag | `frontend/src/app/features/settings/*.ts` | P-1011, P-516, P-708 | med | unit test |
| P-1121 | 11 | Admin screen: branches, users, roles, permission matrix | `frontend/src/app/features/admin/*.ts` | P-1011, P-310, P-311 | high | unit test |
| P-1122 | 11 | Backup/restore screen (export/import UI) | `frontend/src/app/features/settings/backup/*.ts` | P-1120, P-901, P-902 | med | unit test |
| P-1206 | 12 | Tenant provisioning CLI (real tenants) + shared `ProvisioningService` | `tools/provision`, `tools/StoreOS.Provisioning`, `docs/RUNBOOK-provisioning.md` | all Phase 1–9 | high | integration test: provision → Owner logs in; re-run refuses |
| P-1201 | 12 | Seed script: demo tenant, 2 branches, users per role, catalog, stock, IMEIs | `tools/seed/SeedDemoTenant.cs` | all Phase 1–9 | high | run seed → assert row counts |
| P-1202 | 12 | E2E smoke test suite (Playwright): full §2 walkthrough | `frontend/e2e/smoke.spec.ts` | P-1201, all Phase 11 | high | `npx playwright test` green |
| P-1203 | 12 | CI: wire integration/concurrency/RLS schema tests + Testcontainers into pipeline | `.github/workflows/ci.yml` extend | P-111, P-512, P-1202 | med | CI run green end-to-end |
| P-1204 | 12 | Backup restore drill automation (monthly CI/staging job) | `.github/workflows/restore-drill.yml` | P-902 | med | manual trigger succeeds |
| P-1205 | 12 | Final Completeness Audit + Remediation Templates | `docs/COMPLETENESS_AUDIT.md`, `docs/REMEDIATION_TEMPLATE.md` | ALL | high | grep clean, full build/test/e2e PASS table |

**Total: 157 prompts** (155 original + P-313 password change/reset + P-1206 tenant provisioning CLI, added in review v2). Ordering differences vs the first pack are listed in `INDEX.md`.

---

## COVERAGE CHECKLIST

### Plan §5 modules
| Module | Prompt IDs |
|---|---|
| Identity & Access | P-104,105,301-313,1002,1003,1005 |
| Tenancy & Branches | P-102,103,310,1006,1007 |
| Catalog | P-401,402,1109,1110 |
| Inventory | P-403-408,413,414,1109-1112 |
| Purchasing | P-409-412,1111 |
| Sales | P-501-525,1101-1108 |
| Customers | P-506,507,601-604,1102,1113 |
| Suppliers | P-411,605-608,1114 |
| Repairs | P-701-708,1117,1118 |
| FinanceOps | P-609-611,1115,1116 |
| Reporting | P-801-809,1119 |
| Platform | P-116,117,901-906,1008,1009,1122,1206 |

### Plan §6 cross-cutting
| Item | Prompt IDs |
|---|---|
| Tenancy & branch scope (§6.1) | P-106,107,309 |
| AuthN/AuthZ (§6.2) | P-301-313,114,115 |
| Never trust frontend (§6.3) | P-505,510 |
| Idempotency (§6.4) | P-117,511,523,520 |
| Audit trail (§6.5) | P-116 |
| Validation/errors/time/money (§6.6) | P-113,114,118 |
| Correlation-ID (§6.7) | P-112,906 |

### Plan §7 domain engines
| Engine | Prompt IDs |
|---|---|
| StandardGst | P-201 |
| MarginSchemeGst + guard | P-202,505 |
| Discount factor | P-203 |
| EMI + DecimalPow | P-204 |

### Plan §8 data model tables
| Table(s) | Prompt IDs |
|---|---|
| tenants, branches | P-102 |
| settings | P-103 |
| users, roles, role_permissions, user_branches, user_role_assignments | P-104 |
| refresh_tokens | P-105 |
| products | P-401 |
| branch_inventory | P-403 |
| sales, sale_items | P-502 |
| customer_payments | P-508 |
| invoice_sequences | P-501 |
| purchases | P-409 |
| inventory_imeis | P-407 |
| inventory_movements | P-405 |
| customers | P-506,601 |
| customer_ledger_entries | P-506 |
| suppliers | P-411 |
| supplier_transactions | P-605 |
| supplier_ledger_entries | P-605 |
| supplier_returns, supplier_return_items | P-607 |
| expenses | P-609 |
| liabilities, liability_payments | P-610 |
| repair_jobs | P-702 |
| repair_job_parts | P-705 |
| repair_job_status_history | P-704 |
| audit_log | P-116 |
| import_batches | P-412 |
| idempotency_keys | P-117 |

### Plan §10 critical transactional flows
| Flow | Prompt IDs |
|---|---|
| 10.1 Checkout | P-501-515 |
| 10.2 Void/refund | P-524,525 |
| 10.3 Subsequent payment | P-523 |
| 10.4 Supplier return | P-608 |
| 10.5 Repair parts | P-705 |
| 10.6 Branch transfer | P-414 |
| 10.7 Estimate→Invoice conversion | P-517-520 |
| 10.8 Nightly reconciliation | P-905 |

### Plan §11 REST API surface
All endpoint groups covered: Auth (P-303-306), Branches (P-310), POS (P-503,513,518,520),
Sales (P-521-525), Customers (P-602-604), Inventory (P-402,404,413), Purchases (P-410,412),
Suppliers (P-606,608), Transfers (P-414), Repairs (P-703,704,705,707), Expenses (P-609),
Liabilities (P-611), Reports (P-801-805), Settings (P-103,516,1120 backend equivalents in
P-1120's consumed endpoints — backend already in P-103/516/708), Platform (P-901,902,1008).

### Plan §12 frontend
Covered fully by P-1001–1122.

### Plan §13 security hardening
BFF tokens (P-1002,1003), RLS (P-108-111), idempotency (P-117), live-DB perm check (P-308),
CORS (P-1001 environment + backend CORS in P-101 extended — see P-101 note), DPDP (P-604).

### Plan §14 devops
Docker (P-002,003), CI (P-005,1203), IaC — **flagged out of scope for prompt automation**
(Terraform/Bicep require live cloud credentials; P-1205 final audit notes this as a manual
follow-up item, not a coding-agent task).

### Plan §15 observability
Correlation-ID (P-112,906), OpenTelemetry (P-002 otel-collector + wired in P-112/P-906),
health checks (bundled into P-101 API skeleton + verified in P-1203 CI smoke).

### Plan §16 testing strategy
Domain unit (P-201-206), module integration (throughout, each prompt's Verification),
concurrency (P-512,702,501), access control (P-107,110,111,309), API contract (P-1001 NSwag
drift check wired into P-1203), E2E (P-1202), restore drills (P-1204).

### Plan §17 migration
P-901-904.

### Requirements §48 invariants
| Invariant | Prompt IDs (implementation + test) |
|---|---|
| 1. No double-selling IMEI | P-407,408,512 |
| 2. No sale above stock | P-406,512 |
| 3. Repair services excluded from stock | P-401 (no branch_inventory rows rule),404,708,905 |
| 4. Historical invoices survive master changes | P-502 (snapshots),512 test |
| 5. Payments are transactions | P-508,523 |
| 6. Voids/refunds preserve history | P-524,525 |
| 7. Supplier returns affect stock | P-608 |
| 8. Returned IMEI unavailable | P-608,407 |
| 9. Estimates are not finalized sales | P-517,518 |
| 10. Financial calculations reconcile | P-802,803,905 |
| 11. Failed transactions atomic | P-109 (txn scope),510,608 |
| 12. Backups don't silently corrupt | P-902 |

**Coverage: 100%.** No plan section or requirement invariant is without an implementing and
verifying prompt ID.

---

## FILE INDEX

Each prompt's full text lives at `prompts/<ID>-<slug>.md`. See directory listing in
`prompts/INDEX.md` for the complete file-to-ID mapping.
