# StoreOS — Coding Standards & Review Guidelines

Reference standards enforced during code review and architecture verification.

---

## 1. MediatR Pipeline & Behaviors

### 1.1 Open Behavior Generic Constraints
- Always constrain open pipeline behaviors with `where TRequest : notnull`:
  ```csharp
  public class CustomBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
      where TRequest : notnull
  ```
- **Rationale**: `where TRequest : IRequest<TResponse>` causes Microsoft Dependency Injection open-generic matching failures for commands implementing non-generic `MediatR.IRequest` (which inherits `IRequest<Unit>`), silently bypassing the behavior.

### 1.2 Pipeline Registration Order
In `Program.cs`, register open behaviors in the verified sequence:
1. `ValidationBehavior<,>`: Pure field validation via FluentValidation; fails fast with `400 Bad Request`.
2. `SerializationRetryBehavior<,>`: Wraps whole transaction; retries on Postgres serialization failure (`40001` / `40P01`).
3. `AuthorizationBehavior<,>`: Claims-based `IRequiresPermission` check; fails fast with `401 Unauthorized` or `403 Forbidden` without database access.
4. `TransactionBehavior<,>`: Opens database transaction; injects Postgres RLS session variables (`app.tenant_id`, `app.branch_ids`, `app.all_branches`).
5. `LiveDbPermissionBehavior<,>`: Uncached live database check for `IHighImpactOperation` commands under the active RLS transaction.
6. `TokenVersionBehavior<,>`: 30-second cached `token_version` revocation check under the active RLS transaction.
7. `IdempotencyBehavior<,>`: Verifies `IIdempotentRequest` inside the active transaction.
8. Request Handler.

*Rule: Any pipeline behavior that accesses database tables protected by Row-Level Security (`users`, `user_role_assignments`, `role_permissions`, etc.) MUST be registered AFTER `TransactionBehavior`.*

### 1.3 Permission Marker Rules
- A command must implement at most ONE permission marker:
  - `IRequiresPermission`: Claims-based check on JWT claims (routine operations).
  - `IHighImpactOperation`: Uncached live database check (destructive/sensitive operations: `sales:void`, `sales:refund`, `inventory:adjustments`, `settings:manage`, `branches:manage`).
- Commands must never implement both.

---

## 2. Session Invalidation & Cache Management

### 2.1 Token Version Invalidation
- When modifying user authentication state (`user.BumpTokenVersion()` or `user.Deactivate()`):
  - Increment the database field.
  - Invalidate the in-memory cache key:
    ```csharp
    _cache.Remove($"token_version:{userId}");
    ```
- **Rationale**: `TokenVersionBehavior` caches the token version for 30 seconds (`token_version:{userId}`). Explicit eviction eliminates the 30-second window for password changes, admin resets, and deactivations.

---

## 3. Database & Entity Framework Core

### 3.1 Row-Level Security Scoping
- All application database access must occur inside transactions where `RlsSessionInterceptor` has set `app.tenant_id`.
- The application role `storeos_app` has `FORCE ROW LEVEL SECURITY`. Queries outside an RLS transaction evaluate `app.tenant_id` as `NULL` and return 0 rows.

### 3.2 Branch-Nullable Composite Keys
- Tables with nullable branch assignments (such as `user_role_assignments` where `branch_id IS NULL` denotes tenant-wide access) use generated zero-sentinel columns in Postgres (`branch_id_key`).
- **Querying**: Avoid materializing entities with nullable composite keys using `.ToListAsync()`. Use scalar projection or SQL `LEFT JOIN` on `branches`:
  ```csharp
  from a in _db.Set<UserRoleAssignment>()
  join b in _db.Set<Branch>() on a.BranchId equals b.Id into branchGroup
  from b in branchGroup.DefaultIfEmpty()
  where a.UserId == userId
  select new { a.RoleId, BranchId = (Guid?)b.Id }
  ```
- **Inserting**: For inserting tenant-wide assignments with `branch_id = null`, use `_db.ExecuteRawSqlAsync(...)` to bypass EF Core key change-tracking constraints.

### 3.3 Nullable Branch RLS Policy Expression
- When creating an RLS policy on a table that has a nullable `branch_id` (representing optional branch attribution, such as `customer_ledger_entries` or `audit_log`), NEVER use `branch_id IS NULL`. `RlsCoverageTests` strictly scans for and forbids `IS NULL` within RLS qual definitions.
- Always use the sentinel UUID pattern:
  ```sql
  COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid) = '00000000-0000-0000-0000-000000000000'::uuid
  ```

### 3.4 Entity-Before-Reservation Invariant in Mutation Handlers
- When creating transactional aggregates with child items that interact with stock/serial services (e.g., `CheckoutHandler` with `Sale` and `SaleItem`), always construct child line entities client-side FIRST so their IDs (`Guid.NewGuid()`) are generated.
- Never pass placeholder `Guid.Empty` into stock or IMEI services (`SellImeiAsync(imeiRowId, saleId, saleItemId, ct)`).
- Pass the real, client-instantiated `item.Id` to preserve referential integrity across the transaction.

---

## 4. HTTP Middleware & Branch Context

### 4.1 Mutation Branch Context Enforcement
- `BranchContextMiddleware` enforces an explicit branch context for all HTTP mutation methods (`POST`, `PUT`, `PATCH`, `DELETE`).
- Requests must supply `X-Branch-Id` or the caller must have a default branch in `user_branches`.
- **Exclusion**: Authentication and session management endpoints (`/api/auth/*`) are excluded from branch context enforcement.

---

## 5. Anti-Enumeration & Security Responses
- Credential failures (wrong password, unknown email, inactive user) must return identical generic 401 responses with code `"invalid_credentials"` and title `"Invalid email or password."`.
- Passwords and temporary credentials must never be logged, included in audit entries, or returned in response bodies.
