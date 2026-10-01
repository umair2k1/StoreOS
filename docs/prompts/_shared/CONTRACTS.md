# StoreOS Cloud — Shared Contracts (single source of truth for interfaces)

Every prompt in this pack refers to these contracts instead of re-inventing them. If a prompt's code sample and this
file disagree, THIS FILE WINS (the samples were patched, but a stray old snippet may remain). Record any deviation in
`docs/BUILD_LOG.md`.

## 1. Layering and project references

```
StoreOS.Domain          -> (nothing)                      entities, TaxEngine, DomainException, Slug, factories
StoreOS.Application     -> Domain                         MediatR handlers, validators, behaviors that need no infrastructure,
                                                          interfaces in Abstractions/, Microsoft.EntityFrameworkCore (types only)
StoreOS.Infrastructure  -> Application, Domain            EF Core DbContexts, Npgsql, Polly, JWT/refresh services, MinIO, QuestPDF,
                                                          TransactionBehavior, SerializationRetryBehavior, interceptors
StoreOS.Api             -> Application, Infrastructure    controllers, middleware, Program.cs (composition root)
StoreOS.Modules         -> all                            per-module DI registration
```
* `StoreOS.Application` NEVER references `StoreOS.Infrastructure` (would be a circular project reference).
  It reaches the database only via `IAppDbContext` / `IReportingDbContext` and reaches everything else via the interfaces below.
* `StoreOS.Infrastructure.csproj` needs `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (P-004).
* Namespace root is `StoreOS.*` everywhere (never `PhoneZone.*`).

## 2. Application/Abstractions catalogue

| Interface / type | Namespace | Defined in | Implemented by (Infrastructure) |
|---|---|---|---|
| `IAppDbContext` (`Set<T>()`, `Database`, `ChangeTracker`, `SaveChangesAsync`) | `StoreOS.Application.Abstractions` | P-101 | `StoreOsDbContext` |
| `IReportingDbContext` (`Set<T>()`) | same | P-119 | `ReportingDbContext` |
| `IAccessScope` (`TenantId, UserId, BranchIds, AllBranches, Permissions, IsAuthenticated, HasTenantContext, HasPermission, HasBranchAccess, UseAnonymousTenant, UseSystemContext`) | same | P-106 | `HttpAccessScope`, `NullAccessScope` |
| `IAnonymousTenantRequest` (`Guid TenantId`) | same | P-106 | commands: Login, Refresh, Logout |
| `IReadOnlyRequest`, `IReportingRequest`, `ISerializableRequest` | same | P-109 | (markers) |
| `ITenantJobRunner` | same | P-109 | `TenantJobRunner` |
| `ITenantResolver`, `TenantPublicProfile` | same | P-102 | `TenantResolver` |
| `ICorrelationContextAccessor` | same | P-112 | `CorrelationContextAccessor` |
| `IRequiresPermission`, `IHighImpactOperation`, `IBranchContext` | same | P-114 / P-308 / P-309 | behaviors / `BranchContext` |
| `IIdempotentRequest`, `IIdempotencyStore`, `IdempotencyBegin`, `IdempotencyReplayFlag` | same / `.Behaviors` | P-117 | `IdempotencyStore` |
| `IJwtTokenService` | same | P-302 | `JwtTokenService` |
| `IRefreshTokenService` + `RefreshTokenFormat` (static, `StoreOS.Application.Identity`) | same | P-304 | `RefreshTokenService` |
| `IInvoiceSequenceAllocator` | same | P-501 | `InvoiceSequenceAllocator` |
| `IObjectStorageService`, `StoredObject` | same | P-901 | `MinioObjectStorageService` |
| `IPermissionChecker`, `IPasswordHasher` | `StoreOS.Application.Identity` | P-115 / P-301 | `PermissionChecker`, `Pbkdf2/Argon2PasswordHasher` |
| `IAuditWriter` | `StoreOS.Application.Platform` | P-116 | `AuditWriter` |
| `IFeatureManager`, `IRequiresRepairsFeature` | `StoreOS.Application.Platform` / `.Abstractions` | P-708 | `TenantFeatureManager` |
| `IInventoryStockService` | `StoreOS.Application.Inventory` | P-406 | `InventoryStockService` |
| `ICustomerLedgerService` | `StoreOS.Application.Customers` | P-507 | `CustomerLedgerService` |
| `ISupplierLedgerService`, `ILiabilityBalanceService`, `IRepairBillingService` | `.Suppliers` / `.FinanceOps` / `.Repairs` | P-606 / P-611 / P-707 | matching services |

## 3. Request pipeline (MediatR), outermost first

```
ValidationBehavior            pure validation, no DB (400)
SerializationRetryBehavior    retries the WHOLE transaction on 40001/40P01 (read-write requests only)   [Infrastructure, P-511]
TransactionBehavior           opens THE transaction, RLS session variables via interceptor, commits     [Infrastructure, P-109]
TokenVersionBehavior          JWT token_version vs cache/DB (P-307)
AuthorizationBehavior         IRequiresPermission (claims) and IHighImpactOperation (live DB check, P-308)
IdempotencyBehavior           IIdempotentRequest, INSIDE the transaction (P-117)
handler
```
Isolation is chosen by the request marker:

| Request | Context | Isolation | Mode |
|---|---|---|---|
| `IReadOnlyRequest` | `StoreOsDbContext` | ReadCommitted | READ ONLY |
| `IReportingRequest` | `ReportingDbContext` | RepeatableRead | READ ONLY |
| `ISerializableRequest` (checkout, estimate conversion) | `StoreOsDbContext` | Serializable | read-write |
| anything else (commands) | `StoreOsDbContext` | RepeatableRead | read-write |

Rules: every `*Query` implements `IReadOnlyRequest` or `IReportingRequest`; handlers never open transactions; handlers are stateless
and contain no non-database side effects (they may be re-executed by the retry behavior); code that must run after commit runs in the controller.

## 4. HTTP middleware order (Program.cs)

`CorrelationId -> ExceptionHandling -> Authentication -> AccessScope -> BranchContext -> Authorization -> controllers`
(there is NO transaction or idempotency HTTP middleware). Rate limiting sits before authentication for `/api/auth/*`.

## 5. Database roles

| Role | Attributes | Used by |
|---|---|---|
| `storeos` (compose `POSTGRES_USER`) / `storeos_owner` (tests) | superuser/owner | migrations (`dotnet ef`, migration bundle), `deploy/db/post-migrate.sql`, provisioning CLI, seed, restore drills |
| `storeos_app` | `NOSUPERUSER NOBYPASSRLS`, DML only, NO update/delete on append-only tables | API, background jobs, every RLS assertion in tests |

`RlsRoleGuard` refuses to start the API when the connected role can bypass RLS. Connection-string keys: `Default`, `Reporting` (both `storeos_app`);
`STOREOS_MIGRATIONS_CONNECTION` (owner) is used only by tooling.

## 6. RLS policy expressions (all tenant tables)

```sql
tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
-- branch-scoped tables additionally:
AND ( COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
      OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[]) )
```
`NULLIF(..., '')` is mandatory: after any `set_config(..., true)` transaction PostgreSQL leaves the setting as an empty string on that pooled
connection and `''::uuid` throws. Never add `OR ... IS NULL`. The `tenants` table itself has no RLS (it is the lookup table for tenant resolution).

## 7. Standard roles (created by `tools/provision`, P-1206; editable afterwards via P-311)

`Y` = granted, `-` = not granted. Codes are those of `PermissionCatalog` (P-115).

| Permission | Owner (tenant-wide) | Regional Manager (tenant-wide) | Branch Manager | Cashier | Technician |
|---|:-:|:-:|:-:|:-:|:-:|
| `pos:checkout` | Y | - | Y | Y | - |
| `sales:discount:apply` | Y | - | Y | - | - |
| `sales:void`, `sales:refund` | Y | - | - | - | - |
| `sales:credit:override` | Y | - | Y | - | - |
| `sales:read:any-branch` | Y | Y | - | - | - |
| `inventory:cost:view`, `reports:profit:view` | Y | Y | Y | - | - |
| `inventory:manage`, `purchases:manage`, `inventory:adjustments` | Y | - | Y | - | - |
| `expenses:manage`, `liabilities:manage` | Y | - | - | - | - |
| `repairs:manage` | Y | - | Y | - | Y |
| `settings:manage`, `users:manage`, `branches:manage` | Y | - | - | - | - |

Plan §6.2 marks several cells "configurable"; the defaults above are decision D-11 (conservative: void/refund only for Owner).

## 8. Error codes -> HTTP status

Single catalog: `StoreOS.Api/Middleware/ErrorCatalog.cs` (P-113). 401: `unauthenticated, invalid_credentials, token_version_stale, invalid_refresh_token,
refresh_token_expired, refresh_token_reuse_detected, tenant_required`. 403: `forbidden, tenant_inactive`. 400: `branch_context_required, idempotency_key_required,
idempotency_key_invalid` (+ FluentValidation). 409: `branch_code_taken, imei_already_active, idempotency_key_reuse, idempotency_in_progress, serialization_conflict`.
404: any code ending `_not_found`. Everything else raised as a `DomainException`: 422. New non-422 codes are added to the catalog, never to the middleware.

## 9. Invoice numbering

* Tax invoice, FY series (default): `<BRANCH>/<FY>/<NNNNNN>`, e.g. `B01/26-27/000042`; branch code 1-3 chars `[A-Z1-9][A-Z0-9]{0,2}` so the number is <= 16 chars (GST Rule 46(b)); series key `FY26-27`; FY = 1 April - 31 March in the tenant time zone.
* Estimate: `<BRANCH>/E/<FY>/<NNNNNN>`, own sequence key `EST-FY26-27`; never consumes the tax series (D-07).
* Daily-timestamp format is a legacy option (23 chars, not Rule 46(b) compliant): warn on selection, CA sign-off required (O-01).
* Repair job numbers: `REP-<BRANCH>-YYYYMMDD-XXXX` (own sequence).
