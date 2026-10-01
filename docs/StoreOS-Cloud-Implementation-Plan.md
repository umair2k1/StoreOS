# StoreOS Cloud — Complete Implementation Plan

**Document type:** Engineering Implementation Plan (from-scratch build)
**Functional source of truth:** `StoreOS-Complete-Product-Requirements.md`
**Stack:** Angular + TypeScript · ASP.NET Core Web API · EF Core · PostgreSQL · Docker · GitHub Actions
**Deployment model:** Cloud-hosted, multi-user, multi-tenant, multi-branch web application

---

## Table of Contents

1. Purpose & Scope
2. Key Design Decisions
3. Product Deltas vs. the Desktop Application
4. System Architecture
5. Repository & Solution Structure
6. Cross-Cutting Backend Concerns
7. Domain Engines
8. Data Model
9. Access Scope & Enforcement
10. Critical Transactional Flows
11. REST API Surface
12. Frontend (Angular)
13. Security Hardening
14. DevOps
15. Observability
16. Testing Strategy
17. Data Migration from the Desktop App
18. Implementation Roadmap
19. Risks & Mitigations
20. Out of Scope for Initial Release
21. Definition of Done

---

## 1. Purpose & Scope

This document specifies the complete implementation of StoreOS as a cloud-based web application: the migration and expansion of an offline-first Electron + SQLite desktop POS/inventory/ledger/repairs system into a server-authoritative, multi-user, multi-tenant, multi-branch retail operating system.

**Tenancy model:**

```text
Tenant (business / retail chain)
  └── Branches (B01, B02, … — physical shops)
        └── Operational data (stock, sales, IMEIs, repairs, expenses)
Tenant-wide data (customers, products, suppliers, liabilities, users, settings defaults)
```

A single-shop tenant gets one seeded branch (invisible in UX unless more exist). A chain tenant has many branches, and higher-authority users (owner, regional manager) hold tenant-wide access across all branches.

Everything else — GST engine, margin scheme, EMI, ledgers, IMEI rules, repair lifecycle, report reconciliation — is implemented as specified in the product requirements, with business logic living in the backend domain layer.

---

## 2. Key Design Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Multi-tenant, shared DB, shared schema** with `tenant_id` everywhere. Each business = one tenant. | Enables tenant density and low cost; also deployable single-tenant per business if required later. |
| 2 | **Multi-branch per tenant.** Stock is per-branch; catalog, customers, suppliers are tenant-wide. | A physical device exists once; a customer can shop at any branch. |
| 3 | **Branch-scoped access via role assignments.** `branch_id NULL` on an assignment = authority over all branches. | Directly supports admins/higher authority viewing other branches' data. |
| 4 | **Customer credit is tenant-global** (sum of outstanding active invoices across branches), with per-branch attribution visible on the ledger. A customer owing at one branch is flagged at every branch. | Business-realistic; per-invoice branch stamps make per-branch views derivable. |
| 5 | **Suppliers/payables:** supplier master tenant-wide; transactions stamped with `branch_id` (nullable = head office); payables reportable per-branch and consolidated. | Purchases happen at branches; bills are often settled centrally. |
| 6 | **Expenses per-branch** (nullable branch = head office). **Liabilities tenant-level.** | Matches financial reality of a chain. |
| 7 | **GSTIN is a branch attribute.** Branches may share one GSTIN or have separate registrations. GSTR-1 export groups by GSTIN. | Indian GST: each place of business can have its own GSTIN; invoice series must be per-branch regardless. |
| 8 | **Cloud-first, not offline-first.** Server is authoritative. PWA shell + locally persisted cart survive refreshes/network blips; checkout requires connectivity. Full offline POS with sync is out of scope for the initial release. | The product requirements themselves anticipate the server-hosted model (multi-device architecture section). |
| 9 | **Modular monolith**, one deployable API. No microservices. | Keep it a modular monolith until there is an actual reason to split it. |
| 10 | **Licensing/machine-locking is replaced** by tenant activation state + real authentication. | Machine fingerprints are meaningless in a browser. |
| 11 | **Vendor-neutral cloud:** managed PostgreSQL, S3-compatible object storage, container hosting (Azure Container Apps / AWS ECS / Cloud Run all work). | Avoids lock-in. |
| 12 | **India-localized:** INR, `en-IN`, GST engine per spec, GSTR-1 export. | Product requirement. |
| 13 | **Void/refund authority is permission-based**, assignable per-branch or tenant-wide — no hard rule in code. | Each chain decides its own control model. |
| 14 | **Strict White-Labeling & Stealth Identity:** The platform name (`StoreOS`) is strictly internal. The app prompts the user for their shop's name during onboarding, and immediately calls itself by the shop's name everywhere (browser title, top bar, login, PWA, receipts, invoices, WhatsApp/SMS). Zero public vendor branding. | Retailers demand their own customer-facing identity; cashiers and customers should see only their local shop brand. |
| 15 | **Unified Transaction Scope for RLS (Reads & Writes):** Every database operation runs inside an explicit transaction (`ReadCommitted` for reads, `Serializable`/`RepeatableRead` for checkout) where `SET LOCAL` configures session variables (`app.tenant_id`, `app.branch_ids`, `app.all_branches`). | RLS evaluates correctly on both reads and writes without connection pooling leakage; prevents 0-row bugs and cross-tenant leakage. |
| 16 | **100% RLS Coverage on All Tenant Tables:** Every table holding `tenant_id` (including tenant-only tables like `customers`, `suppliers`, `products`) gets an active RLS policy. Enforced by an automated CI schema test. | Prevents raw-SQL queries or missing EF query filters from leaking customer PII or financial data tenant-wide. |
| 17 | **BFF Token Security (In-Memory Access Token + HttpOnly Refresh Cookie):** 15-minute access token kept strictly in Angular memory; 30-day rotating refresh token kept in an `HttpOnly`, `SameSite=Strict`, `Secure` cookie. Token family reuse detection revokes all sessions on replay. | Eliminates XSS token exfiltration; protects against refresh token theft. |
| 18 | **Subdomain-Per-Tenant White-Labeling (`*.pos.storeos.io`):** Wildcard DNS/TLS and tenant subdomains (`<tenant>.pos.storeos.io`) provide browser origin isolation and dynamic W3C Web Manifest generation. | Ensures PWA installs onto cashier devices with the shop's custom brand name and icon without cross-tenant origin collisions. |
| 19 | **Dedicated Reporting Seam (`ReportingDbContext`):** Heavy analytics, dashboards, and GSTR-1 exports are routed through a separate `ReportingDbContext` with its own connection string (`ConnectionStrings:Reporting`). | Allows seamless zero-code routing to a PostgreSQL read replica as tenant volume grows, keeping checkout hot-paths uncontended. |
| 20 | **DPDP Act 2023 & GST 72-Month Invariant:** StoreOS operates as Data Processor; tenant is Data Fiduciary. Customer PII can be anonymized upon statutory erasure request, while fiscal tax invoices remain immutable for the 72-month GST retention period. | Reconciles India's DPDP Act privacy obligations with mandatory Section 36 CGST statutory record retention. |

---

## 3. Product Deltas vs. the Desktop Application

| Desktop baseline | Cloud implementation |
|---|---|
| Electron + SQLite, local source of truth | ASP.NET Core API + PostgreSQL, server is source of truth |
| Single user, app-level master password | Multi-user: JWT + refresh, roles + granular permissions, per-branch scoping |
| Machine fingerprint + HMAC licensing | Tenant activation flag; disabled tenant blocks login/use |
| Local JSON backup/restore | (a) Platform: managed DB PITR + nightly dumps; (b) per-tenant JSON export/import to object storage — which doubles as the desktop→cloud data migration path |
| Local printing | Server-rendered PDF (QuestPDF) + browser print CSS; templates receive DTOs only |
| `BEGIN IMMEDIATE` SQLite serialization | PostgreSQL conditional updates + row locking + unique constraints — same guarantees |
| Single shop | Multi-tenant with RLS, multi-branch with scoped access |
| Repair module feature flag | Same: per-tenant feature flag; disabling hides workflows, never deletes history |

---

## 4. System Architecture

```text
                        ┌───────────────────────────────────────────────┐
   Browser (Angular     │  CDN / Edge Ingress (nginx / Cloudflare)      │
   PWA, desktop &       │  Host: *.pos.storeos.io (subdomain per tenant)│
   tablet)              └───────────────────────┬───────────────────────┘
                                                │ HTTPS, In-Memory JWT, HttpOnly Cookie,
                                                │ X-Branch-Id, X-Correlation-ID
                        ┌───────────────────────▼───────────────────────┐
                        │  ASP.NET Core modular monolith (API)          │
                        │  ┌─────────────────────────────────────────┐  │
                        │  │ Modules: Identity, Tenancy & Branches,  │  │
                        │  │ Catalog, Inventory, Sales, Customers,   │  │
                        │  │ Suppliers, Repairs, Expenses,           │  │
                        │  │ Liabilities, Reporting, Platform        │  │
                        │  └─────────────────────────────────────────┘  │
                        │  CQRS · Hangfire · Dynamic W3C Manifest       │
                        │  StoreOsDbContext (Writes/POS)                │
                        │  ReportingDbContext (Read Replica Seam)       │
                        └───┬───────────────────┬──────────────┬────────┘
                            │                   │              │
             ┌──────────────┴───────┐     ┌─────▼────────┐  ┌──▼───────────────┐
             │ PostgreSQL (Managed) │     │ S3 Object    │  │ OTLP collector   │
             │ ┌──────────────────┐ │     │ Storage      │  │ → Grafana /      │
             │ │ Primary Writer   │ │     │ (SSE-KMS,    │  │   App Insights   │
             │ │ (StoreOsDbContext│ │     │  WORM lock,  │  └──────────────────┘
             │ │  RLS enabled)    │ │     │  private)    │
             │ └─────────┬────────┘ │     └──────────────┘
             │           │ WAL      │
             │ ┌─────────▼────────┐ │
             │ │ Read Replica     │ │
             │ │ (ReportingContext│ │
             │ │  dashboards/GSTR)│ │
             │ └──────────────────┘ │
             └──────────────────────┘
```

**Runtime topology per environment:**
1. **Frontend / Edge Hosting:** Dynamic tenant subdomains (`https://<tenant>.pos.storeos.io`) served via CDN with wildcard DNS/TLS (`*.pos.storeos.io`). Each tenant runs in its own browser origin, isolating PWA installations and caches.
2. **API & Background Jobs:** ASP.NET Core API container(s) hosting modular endpoints and background workers (Hangfire).
3. **Database Topology & Reporting Seam:**
   - **Operational Primary Writer:** Handles POS checkouts, inventory updates, and transactional commands via `StoreOsDbContext` within ambient transactions with `SET LOCAL` RLS parameters.
   - **Reporting Replica Seam:** Read-heavy aggregates (dashboard queries, GSTR-1 exports, inventory valuation) execute through `ReportingDbContext` (`QueryTrackingBehavior.NoTracking`), configured on Day 1 to point to `ConnectionStrings:Reporting`. As tenant volume grows, this connection string is repointed to a PostgreSQL read replica without code changes, isolating the checkout hot path from analytical contention.
4. **CORS Architecture:**
   - The API explicitly enforces CORS against incoming tenant subdomains using a dynamic origin validator matching `^https://([a-z0-9-]+)\.pos\.storeos\.io$` and `^https://pos\.storeos\.io$` (plus `http://localhost:4200` in dev).
   - Supported methods: `GET, POST, PUT, PATCH, DELETE, OPTIONS`.
   - Allowed headers: `Content-Type, Authorization, X-Branch-Id, X-Correlation-ID, Idempotency-Key, X-Requested-With`.
   - Exposed headers: `X-Correlation-ID, Idempotency-Key, Content-Disposition`.
   - `AllowCredentials: true` is strictly enabled to permit transmission of the `SameSite=Strict` refresh token cookie during auth flows.
5. **Object Storage:** S3-compatible private storage encrypted at rest via SSE-KMS, with 30-day WORM Object Lock for backups and sanitized tenant branding assets.

---

## 5. Repository & Solution Structure

Monorepo:

```text
store-os-cloud/
├── backend/
│   ├── src/
│   │   ├── StoreOS.Api/               # composition root, middleware, endpoints
│   │   ├── StoreOS.Domain/            # pure domain: per-module folders
│   │   ├── StoreOS.Application/       # commands/queries, validation, behaviors
│   │   ├── StoreOS.Infrastructure/    # EF Core, Npgsql, storage, jobs, PDF
│   │   └── StoreOS.Modules/           # module registration (per-module DI)
│   └── tests/
│       ├── StoreOS.Domain.Tests/      # tax, EMI, valuation — no DB
│       ├── StoreOS.IntegrationTests/  # Testcontainers PostgreSQL (RLS enabled)
│       └── StoreOS.ConcurrencyTests/  # parallel checkout, numbering, IMEI
├── frontend/                          # Angular workspace (see §12)
├── deploy/
│   ├── docker-compose.yml             # dev: api, postgres, minio, otel-collector
│   ├── Dockerfile.api / Dockerfile.web
│   └── infra/                         # Terraform/Bicep
└── docs/
```

Each module folder inside Domain/Application/Infrastructure keeps its own entities, commands, handlers, and EF configuration. **Module boundaries are enforced by internal visibility and an `InternalsVisibleTo` allow-list**, not by physical repos.

**Module list (bounded contexts):**

```text
Identity & Access     users, roles, permissions, sessions, branch assignments
Tenancy & Branches    tenants, branches, settings, feature flags, activation
Catalog               products (incl. repair services), categories
Inventory             branch stock, IMEIs, movements, transfers, valuation
Purchasing            purchases, supplier invoice import
Sales                 POS, invoices, estimates, payments, voids/refunds, finance/EMI
Customers             master, ledger, credit
Suppliers             master, transactions, payables, returns
Repairs               services, jobs, parts, status history
FinanceOps            expenses, liabilities
Reporting             dashboards, daily audit, comparison, exports, GSTR-1
Platform              backup/export/import, idempotency, audit, jobs, health
```

### 5.1 Architectural patterns & boundaries

- **Clean Architecture:** Core layers `Domain` → `Application` → `Infrastructure` → `Api`. Pure domain logic (`PhoneZone.Domain.Tax`, `.Finance`) remains 100% dependency-free with zero DB or framework references.
- **CQRS-lite:** CQRS is applied pragmatically using MediatR commands and queries against a single relational database. No event sourcing, no separate read-model database, no unnecessary eventual consistency.
- **Tactical DDD:** Aggregate roots protect invariants (e.g., `Sale` aggregate root encapsulates `SaleItem`s and line-item tax calculations; `RepairJob` aggregate root controls parts reservation and status state machine). Skip "strategic DDD" ceremony (context map workshops, ubiquitous-language documents) — focus strictly on invariant protection in entities.
- **Vertical Slice within Application layer:** Rather than horizontal technical-role folders (`Commands/`, `Queries/`, `Handlers/`) that force multi-folder navigation for a single feature, organize `PhoneZone.Application/<Module>/` by **use case**:
  ```text
  PhoneZone.Application/
  └── Sales/
      ├── Checkout/
      │   ├── CheckoutCommand.cs
      │   ├── CheckoutHandler.cs
      │   └── CheckoutValidator.cs
      ├── VoidSale/
      │   ├── VoidSaleCommand.cs
      │   ├── VoidSaleHandler.cs
      │   └── VoidSaleValidator.cs
      └── GetSaleById/
          ├── GetSaleByIdQuery.cs
          └── GetSaleByIdHandler.cs
  ```
  This delivers the cohesion and discoverability of Vertical Slice Architecture without violating Clean Architecture layer boundaries.
- **Cross-module interaction via internal application services:** Cross-module writes must NOT directly mutate foreign tables. When a use-case in one module requires effects in another (e.g. Sales checkout decrementing inventory, recording customer ledger debits, or linking repair tickets), the coordinating module invokes **typed internal application-service interfaces** exposed by the respective modules:
  - `IInventoryStockService.ReserveAndDecrementStockAsync(...)`
  - `IInventoryStockService.SellImeisAsync(...)`
  - `ICustomerLedgerService.RecordInvoiceDebitAsync(...)`
  - `IRepairBillingService.LinkSaleAsync(...)`
  All module services share the same DbContext / ambient EF Core transaction unit-of-work, preserving single-transaction atomicity while strictly respecting module boundaries and preventing Sales from becoming an unchecked "God module".
- **Feature flag management:** Do not scatter raw `tenant.Features["repairs"]` dictionary checks throughout handlers. Wrap tenant feature checks behind an `IFeatureManager` service (leveraging `Microsoft.FeatureManagement`). Maintain a strict conceptual separation between **Business Feature Flags** (tenant-controlled module entitlements stored in `tenants.features jsonb`) and **Release Toggles** (developer/deployment-controlled rollout switches configured via appsettings/environment variables).

---

## 6. Cross-Cutting Backend Concerns

### 6.1 Tenancy & branch scope

- Every business table has `tenant_id uuid NOT NULL`; branch-scoped tables additionally have `branch_id`.
- JWT carries `tenant_id`, visible `branch_ids`, and an `all_branches` flag.
- An `IAccessScope` scoped service exposes: `TenantId`, `BranchIds`, `AllBranches`, plus permission resolution per branch context.
- Every request resolves an access scope **before** any handler runs.

### 6.2 Authentication & authorization

- ASP.NET Core Identity for users per tenant; passwords hashed (PBKDF2/Argon2), never plaintext.
- **JWT access token (15 min) + rotating refresh tokens (30 days)**, revocable.
- **Roles are bundles of granular permissions. Permission checks are the enforcement unit.**

| Permission | Owner/Admin (all branches) | Regional Mgr (multi-branch) | Branch Mgr (one branch) | Cashier | Technician |
|---|:--:|:--:|:--:|:--:|:--:|
| `pos:checkout` | ✅ | — | ✅ | ✅ | — |
| `sales:discount:apply` | ✅ | — | configurable | configurable | — |
| `sales:void`, `sales:refund` | ✅ | — | configurable | — | — |
| `inventory:cost:view`, `reports:profit:view` | ✅ | ✅ | configurable | ❌ | ❌ |
| `inventory:manage`, `purchases:manage` | ✅ | — | ✅ | — | — |
| `expenses:manage`, `liabilities:manage` | ✅ | — | — | — | — |
| `repairs:manage` | ✅ | — | ✅ | — | ✅ |
| `settings:manage`, `users:manage`, `branches:manage` | ✅ | — | — | — | — |
| Cross-branch read (`sales:read:any-branch` etc.) | ✅ | ✅ | ❌ | ❌ | ❌ |

- Authorization runs in a MediatR pipeline **authorization behavior** — never controllers, never the frontend.
- **Sensitive-data masking is orthogonal to branch visibility:** a regional manager with cross-branch read but no `inventory:cost:view` still receives DTOs with costs masked.

### 6.3 Never trust the frontend

Checkout accepts: product ids, quantities, IMEI selections, customer id, payment info, flags (gift, margin scheme), and **authorized** price overrides only. The server then:

1. Loads products/IMEIs from DB (tenant- and branch-filtered).
2. Recomputes prices, taxes, totals from DB master data — client-submitted prices/totals/discounts are ignored unless an override permission exists.
3. Runs FluentValidation and domain rules.

### 6.4 Idempotency

```sql
CREATE TABLE idempotency_keys (
  tenant_id  uuid NOT NULL,
  user_id    uuid NOT NULL,
  key        text NOT NULL,
  endpoint   text NOT NULL,
  request_hash text NOT NULL,
  response_status int,
  response_body  jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, user_id, endpoint, key)
);
```

Middleware on `POST /sales/checkout` and payment endpoints: client sends an `Idempotency-Key` header (UUID). The primary key explicitly scopes `key` by `endpoint`. If a client or buggy retry reuses the same UUID against different endpoints, scoping to `endpoint` prevents the second call from colliding with the first call's cached record before the payload hash check can evaluate. Same key + same endpoint + same payload hash → replay stored response. Same key + same endpoint + different payload → `409 Conflict`.

### 6.5 Audit trail

Append-only `audit_log` (see §8.10). The application's DB role has **no UPDATE/DELETE grant** on `audit_log` or ledger tables. Every money/stock event additionally writes to domain ledgers (§8.9).

### 6.6 Validation, errors, time, money

- FluentValidation via MediatR pipeline; failures → `400` with field-level problems.
- Domain rule violations → typed `DomainException` → `422` with code + message. Never a 500.
- **Money: `numeric(18,2)`, `decimal` in C#, never double.** Round half-up (`MidpointRounding.AwayFromZero`), 2 dp.
- Timestamps: `timestamptz` in UTC; business date computed in the tenant's timezone (`Asia/Kolkata` default) — critical for "today's revenue" and daily sequences.
- `en-IN` / INR formatting only at the presentation layer.

### 6.7 Request Correlation-ID & Observability Context

Every HTTP request must carry a unique correlation identifier for end-to-end tracing, auditing, and support:

1. **Ingestion & generation:** An early ASP.NET Core `CorrelationIdMiddleware` inspects incoming headers for `X-Correlation-ID`. If present and valid, it is adopted; otherwise, the server generates a new `Guid.NewGuid().ToString("D")`.
2. **Response header:** The server always echoes `X-Correlation-ID: <id>` in HTTP response headers.
3. **Structured logging:** Middleware enriches Serilog's `LogContext` (`LogContext.PushProperty("CorrelationId", correlationId)`). Every log statement emitted during the request lifecycle automatically contains the correlation ID.
4. **Diagnostic context & tracing:** The ID is set on `HttpContext.Items["CorrelationId"]`, exposed via a scoped `ICorrelationContext` accessor, and attached to OpenTelemetry `Activity.Current` baggage/tags.
5. **Background job propagation:** When asynchronous tasks or Hangfire jobs are scheduled from a request, the correlation ID is passed into job arguments and rehydrated into the background execution context.
6. **Audit trail linkage:** The correlation ID is recorded in `audit_log.correlation_id` (§8.10), connecting business audit records directly to technical log streams.
7. **Support-ready error responses:** In accordance with RFC 7807 `ProblemDetails`, all error responses (4xx, 5xx) include `extensions["correlationId"] = correlationId`. Cashiers encountering an error can quote this ID to support for instant root-cause lookup.

---

## 7. Domain Engines

`PhoneZone.Domain.Tax`, `.Finance` — pure, no dependencies, no DB.

### 7.1 GST (standard, tax-inclusive)

```csharp
public static LineTax StandardGst(decimal lineTotal, decimal gstRate)
{
    var taxable = Money.Round2(lineTotal / (1 + gstRate / 100m));
    var totalGst = Money.Round2(lineTotal - taxable);
    var cgst = Money.Round2(totalGst / 2m);
    var sgst = Money.Round2(totalGst - cgst);   // halves always reconcile to totalGst
    return new LineTax(taxable, cgst, sgst);
}
```

### 7.2 Margin scheme (used phones only — enforced by a domain guard)

```csharp
public static LineTax MarginSchemeGst(decimal sell, decimal cost, decimal gstRate)
{
    // Hard domain validation: cost basis MUST be strictly positive.
    // Silent fallback to cost = 0 is prohibited (would treat full selling price as profit margin,
    // miscalculating GST and creating severe statutory audit liability under CGST Rule 32(5)).
    if (cost <= 0)
        throw new DomainValidationException("Margin scheme requires a known positive cost basis (> 0). Cost cannot be zero or empty.");

    var margin = sell - cost;
    if (margin <= 0) return LineTax.Zero;
    var taxable = Money.Round2(margin / (1 + gstRate / 100m));
    var totalGst = Money.Round2(margin - taxable);
    var cgst = Money.Round2(totalGst / 2m);
    return new LineTax(taxable, cgst, Money.Round2(totalGst - cgst));
}
```

**Margin-scheme cost source & mandatory cost rule:** Margin scheme is a per-sale-line toggle valid strictly for `UsedPhone` items. Because acquisition cost varies unit-by-unit:
- **When an IMEI is present on the line:** `cost = inventory_imeis.purchase_price`. If `inventory_imeis.purchase_price` is NULL or `<= 0`, checkout immediately fails with a hard validation error (`422 UnprocessableEntity`: *"Margin scheme sale for IMEI {imei} rejected: no valid positive purchase cost recorded."*).
- **When no IMEI is present:** `cost = branch_inventory.cost_price > 0 ? branch_inventory.cost_price : products.purchase_price`. If neither cost basis is seeded (`<= 0`), checkout immediately fails with `422 UnprocessableEntity`: *"Margin scheme requires a known positive cost basis for product '{product.name}'. Please update inventory cost before billing under margin scheme."*
- **No silent zero fallback:** Treating unseeded cost as 0 would compute `margin = sell` and miscalculate tax as if the entire selling price were gross margin. A hard validation error forces data correction before an invoice can be finalized.

### 7.3 Invoice discount (proportional)

```csharp
decimal grand = subtotal - discount;   // validated: 0 <= discount <= subtotal
decimal f = grand / subtotal;
taxableAfter = Round2(taxableBefore * f);
cgstAfter    = Round2(cgstBefore * f);
```

### 7.4 EMI (pure decimal calculation)

```csharp
decimal principal = grandTotal - downPayment;  // validated: principal > 0, downPayment <= grandTotal
if (annualRate == 0) return Money.Round2(principal / months);
decimal r = annualRate / 12m / 100m;

// Pure decimal exponentiation for integer tenure months (never cast to double):
decimal pow = DecimalPow(1m + r, months);
emi = Money.Round2(principal * r * pow / (pow - 1m));

private static decimal DecimalPow(decimal baseVal, int exp)
{
    decimal result = 1m;
    for (int i = 0; i < exp; i++)
    {
        result *= baseVal;
    }
    return result;
}
```
*Note: Avoiding `Math.Pow` eliminates floating-point IEEE 754 precision loss and ensures strict adherence to the codebase's 'never double, decimal only' financial calculation invariant.*

---

## 8. Data Model (PostgreSQL)

### 8.1 Conventions

- PKs `uuid` (server-generated). Ledger/audit tables: `bigint GENERATED ALWAYS AS IDENTITY`.
- Money `numeric(18,2)`; quantities `int` with check constraints.
- Enums as `text` with check constraints (valid values per the product spec's integrity section).
- Snapshot fields on sale lines: historical invoices remain understandable even if master data changes.
- Indexes lead with `tenant_id` (and `branch_id` where queries are branch-scoped).

### 8.2 Tenancy, branches, settings

```sql
CREATE TABLE tenants (
  id uuid PRIMARY KEY,
  name text NOT NULL,
  is_active bool NOT NULL DEFAULT true,       -- activation/subscription state
  timezone text NOT NULL DEFAULT 'Asia/Kolkata',
  features jsonb NOT NULL DEFAULT '{}',       -- e.g. {"repairs": true}
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE branches (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL,                          -- 'B01' → numbering prefixes
  name text NOT NULL,
  address text, phone text, email text,
  gstin text,                                  -- may differ per branch
  is_active bool NOT NULL DEFAULT true,        -- soft-disable only; history never deleted
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, code)
);

CREATE TABLE settings (
  tenant_id uuid NOT NULL,
  branch_id uuid NULL REFERENCES branches(id), -- NULL = tenant-level default
  key text NOT NULL,
  value jsonb NOT NULL,
  PRIMARY KEY (tenant_id, COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid), key)
);
-- Branch-level: shop profile (name, address, phone, email, GSTIN), invoice prefix.
-- Tenant-level: default GST rate, bank config, finance providers, repair feature flag.
```

### 8.3 Identity & access

```sql
CREATE TABLE users (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  email text NOT NULL,
  password_hash text NOT NULL,
  display_name text NOT NULL,
  token_version int NOT NULL DEFAULT 1,        -- incremented on role changes/logout to invalidate active JWTs
  is_active bool NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, email)
);

CREATE TABLE roles (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL
);

CREATE TABLE role_permissions (
  role_id uuid NOT NULL REFERENCES roles(id),
  permission_code text NOT NULL,               -- global seeded permission catalog
  PRIMARY KEY (role_id, permission_code)
);

CREATE TABLE user_branches (
  user_id uuid NOT NULL REFERENCES users(id),
  branch_id uuid NOT NULL REFERENCES branches(id),
  is_default bool NOT NULL DEFAULT false,
  PRIMARY KEY (user_id, branch_id)
);

CREATE TABLE user_role_assignments (
  user_id uuid NOT NULL REFERENCES users(id),
  role_id uuid NOT NULL REFERENCES roles(id),
  branch_id uuid NULL REFERENCES branches(id), -- NULL = tenant-wide (all branches)
  PRIMARY KEY (user_id, role_id, branch_id)
);

CREATE TABLE refresh_tokens (
  id uuid PRIMARY KEY,
  user_id uuid NOT NULL REFERENCES users(id),
  token_hash text NOT NULL,
  family_id uuid NOT NULL,                     -- tracks token lineage for theft/reuse detection
  is_revoked bool NOT NULL DEFAULT false,
  replaced_by_token_id uuid NULL REFERENCES refresh_tokens(id),
  expires_at timestamptz NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_refresh_tokens_family ON refresh_tokens (family_id);
CREATE INDEX ix_refresh_tokens_lookup ON refresh_tokens (user_id, token_hash);
```

**Refresh Token Theft & Reuse Detection:**
- Refresh tokens use rotating token families (`family_id`).
- When a refresh token is presented to `POST /auth/refresh`, if it has already been rotated (`is_revoked = true` or `replaced_by_token_id IS NOT NULL`), the server identifies this as a token replay/theft attack.
- The server immediately revokes the **entire family** (`UPDATE refresh_tokens SET is_revoked = true WHERE family_id = @familyId`), increments `users.token_version`, logs a critical security alert, and forces re-authentication across all user devices.

**Stale Permissions & 15-Minute JWT Window Mitigation:**
- Standard queries trust claims (`BranchIds`, `Permissions`) inside the short-lived 15-minute access token validated against `token_version`.
- When an administrator modifies a user's role assignments or deactivates an account, the backend immediately increments `users.token_version` and invalidates any active refresh token families.
- A fast distributed cache check (or 30s in-memory cache) rejects tokens where `claim.ver != cached.token_version`.
- **High-Impact Operations:** Sensitive actions (`sales:void`, `sales:refund`, `inventory:adjustments`, `settings:manage`, `branches:manage`) **bypass token claims and execute a live, direct DB query** against `user_role_assignments` and `role_permissions` inside the transaction, guaranteeing zero lag on permission revocation.

**Effective access per request:**

```text
BranchIds  = assigned branches ∪ branches covered by NULL-scope assignments
             ("AllBranches" flag if any NULL-scope assignment exists)
Permission(product, branch X) = grants from NULL-scope assignments ∪ grants for branch X
```

### 8.4 Catalog & branch inventory

The catalog/stock split: the same product exists once tenant-wide but holds stock at N branches. Repair services live in the same catalog with category `RepairService` — **they simply never get `branch_inventory` rows**, so they can never inflate stock, cost, or valuation (product invariant 3).

```sql
CREATE TABLE products (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  category text NOT NULL CHECK (category IN ('NewPhone','UsedPhone','Accessory','RepairService')),
  sell_price numeric(18,2) NOT NULL,
  purchase_price numeric(18,2) NOT NULL DEFAULT 0,     -- default/last cost, convenience only
  gst_rate numeric(5,2) NOT NULL DEFAULT 18,
  is_taxable bool NOT NULL DEFAULT true,
  is_margin_scheme bool NOT NULL DEFAULT false,        -- valid only for UsedPhone (domain-enforced)
  hsn_code text,
  description text,                                    -- service-oriented description
  estimated_duration_minutes int,                      -- service-only
  warranty_days int,                                   -- service-only
  is_active bool NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_products_tenant_cat  ON products (tenant_id, category);
CREATE INDEX ix_products_tenant_name ON products (tenant_id, name);  -- trigram for fast search

CREATE TABLE branch_inventory (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  stock_qty int NOT NULL DEFAULT 0 CHECK (stock_qty >= 0),
  cost_price numeric(18,2) NOT NULL DEFAULT 0,         -- branch cost basis
  sell_price_override numeric(18,2) NULL,              -- optional branch-specific retail price override
  low_stock_threshold int NOT NULL DEFAULT 3,
  UNIQUE (tenant_id, branch_id, product_id)
);
-- Rule: pricing engine resolves branch_inventory.sell_price_override ?? products.sell_price.
-- Rule (app-enforced + nightly reconciliation): no branch_inventory rows for RepairService.
```

### 8.5 Core POS tables

```sql
CREATE TABLE sales (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  invoice_number text NOT NULL,
  invoice_type text NOT NULL CHECK (invoice_type IN ('TaxInvoice','Estimate')),
  sale_date timestamptz NOT NULL,
  customer_id uuid NULL,
  customer_name text, customer_gstin text,             -- snapshots
  total_taxable numeric(18,2) NOT NULL,
  total_cgst numeric(18,2) NOT NULL,
  total_sgst numeric(18,2) NOT NULL,
  grand_total numeric(18,2) NOT NULL,
  discount numeric(18,2) NOT NULL DEFAULT 0,
  amount_paid numeric(18,2) NOT NULL DEFAULT 0,        -- projection of payments
  status text NOT NULL DEFAULT 'Active' CHECK (status IN ('Active','Voided','Refunded')),
  payment_mode text CHECK (payment_mode IN ('Cash','UPI','Card','Credit','Finance')),
  finance_provider text, financer_name text,
  down_payment numeric(18,2), emi_details jsonb,
  gift_details jsonb, description text,
  repair_job_id uuid NULL,                             -- repair billing link (see §8.8 for two-step FK resolution)
  irn text NULL,                                       -- E-invoicing IRN (64-char hash via IRP)
  irn_ack_no text NULL,                                -- IRP Acknowledgment Number
  irn_qr_data text NULL,                               -- Signed QR payload from IRP
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, invoice_number)                   -- global backstop
);
CREATE INDEX ix_sales_tenant_branch_date ON sales (tenant_id, branch_id, sale_date DESC);
CREATE INDEX ix_sales_tenant_status ON sales (tenant_id, status);
CREATE INDEX ix_sales_tenant_irn ON sales (tenant_id, irn) WHERE irn IS NOT NULL;

-- E-invoicing Readiness & PAN Threshold Rule:
-- GST e-invoicing (IRN generation via IRP) is mandatory once aggregate business turnover under one PAN crosses
-- ₹5 crore in any financial year since 2017-18, and this threshold is permanent. Turnover aggregates across ALL branches
-- under the tenant's PAN (e.g. 4 branches doing ₹1.5cr each cross the ₹5cr threshold as an entity). Nullable columns
-- irn, irn_ack_no, and irn_qr_data are included on day one, and invoice PDF templates reserve space for the IRN and
-- B2B signed QR code, enabling Phase 8 e-invoicing activation without altering historical tables.

CREATE TABLE sale_items (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  sale_id uuid NOT NULL REFERENCES sales(id),
  product_id uuid NULL,                    -- nullable: deleted masters / ad-hoc lines
  item_name text NOT NULL,                 -- snapshots (product invariant 4)
  category text NOT NULL,
  qty int NOT NULL CHECK (qty > 0),
  price_per_unit numeric(18,2) NOT NULL,
  purchase_price_snapshot numeric(18,2),
  hsn_snapshot text,
  is_margin_scheme bool NOT NULL DEFAULT false,
  is_gift bool NOT NULL DEFAULT false,
  taxable numeric(18,2) NOT NULL,
  cgst numeric(18,2) NOT NULL,
  sgst numeric(18,2) NOT NULL,
  imei text
);
CREATE INDEX ix_sale_items_sale ON sale_items (tenant_id, sale_id);
CREATE INDEX ix_sale_items_imei ON sale_items (tenant_id, imei);

CREATE TABLE customer_payments (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  sale_id uuid NOT NULL REFERENCES sales(id),
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  payment_mode text NOT NULL,
  payment_date date NOT NULL,
  received_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_custpay_sale ON customer_payments (tenant_id, sale_id);
```

**Invoice numbering under concurrency — GST Rule 46(b) & sequences:**

Rule 46(b) of the CGST Rules mandates a *consecutive serial number, not exceeding sixteen characters, in one or multiple series, unique for a financial year*.
- **Daily-reset format (`<BRANCH>-YYYYMMDD-HHMMSS-NNN`):** Resets daily; consecutive within a day, but not across the entire financial year. While common in retail POS, it poses an audit risk if an auditor strictly enforces Rule 46(b).
- **Financial Year Consecutive Series (`<BRANCH>/<FY>/<NNNNNN>`):** e.g., `B01/26-27/000124` (<= 16 characters, consecutive across the FY per branch series). Fully compliant with Rule 46(b).
- **CA Confirmation Item:** The sequence engine is parameterized via branch settings (`invoice_series_format`). Tenants must explicitly confirm their invoice series format with their Chartered Accountant (CA) before launch.

```sql
CREATE TABLE invoice_sequences (
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  series_key text NOT NULL,                 -- e.g. 'FY26-27' or '20260923'
  last_seq int NOT NULL DEFAULT 0,
  PRIMARY KEY (tenant_id, branch_id, series_key)
);
```

Inside the checkout transaction (atomic in PostgreSQL):

```sql
INSERT INTO invoice_sequences (tenant_id, branch_id, series_key, last_seq)
VALUES (@t, @b, @seriesKey, 1)
ON CONFLICT (tenant_id, branch_id, series_key)
DO UPDATE SET last_seq = invoice_sequences.last_seq + 1
RETURNING last_seq;
```

Formats supported:
1. Standard FY Series (Default/Recommended): `<BRANCH>/<FY>/<NNNNNN>` (e.g. `B01/26-27/000042`).
2. Daily Timestamp Series (Legacy POS): `<BRANCH>-YYYYMMDD-HHMMSS-NNN` (e.g. `B01-20260923-143005-007`).

The global unique index `UNIQUE (tenant_id, invoice_number)` plus a retry loop is the backstop. Repair job numbers follow the same pattern: `REP-<BRANCH>-YYYYMMDD-XXXX` or `REP-<BRANCH>/<FY>/<NNNNNN>`.

### 8.6 Purchases, IMEIs, movements

```sql
CREATE TABLE purchases (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  qty int NOT NULL CHECK (qty > 0),
  purchase_rate numeric(18,2) NOT NULL,
  supplier_id uuid NULL,
  supplier_name text, supplier_phone text, supplier_address text,  -- snapshots
  seller_name text, seller_phone text, seller_address text,
  supplier_invoice_no text,
  import_batch_id uuid NULL,               -- dedupe re-imported supplier invoices
  purchase_date date NOT NULL,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_purchases_tenant_branch_product ON purchases (tenant_id, branch_id, product_id);
CREATE INDEX ix_purchases_tenant_branch_date ON purchases (tenant_id, branch_id, purchase_date);

CREATE TABLE inventory_imeis (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  purchase_id uuid NULL,
  imei text NOT NULL,
  status text NOT NULL CHECK (status IN ('InStock','Sold','ReturnedToSupplier','InTransit')),
  sale_id uuid NULL, sale_item_id uuid NULL,
  return_id uuid NULL,
  purchase_date date, purchase_price numeric(18,2),
  created_at timestamptz NOT NULL DEFAULT now()
);
-- A physical device exists once per tenant: active-uniqueness is TENANT-wide, not branch-wide
CREATE UNIQUE INDEX ux_imei_active ON inventory_imeis (tenant_id, imei)
  WHERE status IN ('InStock','Sold','InTransit');
CREATE INDEX ix_imei_lookup ON inventory_imeis (tenant_id, imei);

CREATE TABLE inventory_movements (              -- append-only; every stock change is one row
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  imei_id uuid NULL,
  movement_type text NOT NULL,   -- Purchase|Sale|SaleVoid|SaleRefund|SupplierReturn|
                                 -- RepairConsumed|RepairPartReturned|Adjustment|TransferOut|TransferIn
  qty_delta int NOT NULL CHECK (qty_delta <> 0),
  unit_cost numeric(18,2) NOT NULL,
  ref_type text NOT NULL,
  ref_id text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  created_by uuid NULL
);
CREATE INDEX ix_movements_tenant_branch_product ON inventory_movements (tenant_id, branch_id, product_id);
```

`branch_inventory.stock_qty` is a transactionally consistent projection of movements; a nightly reconciliation job asserts they agree and alerts on drift.

### 8.7 Customers, suppliers, payables

```sql
CREATE TABLE customers (                        -- tenant-wide
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  phone text,
  gstin text,
  credit_limit numeric(18,2) NULL,             -- tenant-wide credit limit (NULL = unlimited)
  desktop_id text NULL,                        -- immutable desktop SQLite PK for incremental pilot sync deduplication
  total_purchases numeric(18,2) NOT NULL DEFAULT 0,    -- projection
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_customers_tenant_phone ON customers (tenant_id, phone);
CREATE INDEX ix_customers_tenant_desktop_id ON customers (tenant_id, desktop_id) WHERE desktop_id IS NOT NULL;

-- Customer Credit Limits & Cross-Branch Visibility:
-- Customer credit is tenant-global and consolidated across all branches. To prevent customers accumulating
-- unbounded debt across different shops before single cashiers realize the full exposure, the POS checkout interface
-- surfaces the live consolidated credit balance across all branches (derived_balance = Σ(unpaid_invoices)).
-- When credit_limit is set and derived_balance + new_credit > credit_limit, the checkout flow triggers a warning
-- or block (configurable per tenant), with an override requiring sales:credit:override permission.

CREATE TABLE customer_ledger_entries (          -- append-only
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  customer_id uuid NOT NULL REFERENCES customers(id),
  sale_id uuid NULL,
  entry_type text NOT NULL,     -- Invoice|Payment|InvoiceVoid|Refund|OpeningBalance
  debit numeric(18,2) NOT NULL DEFAULT 0,
  credit numeric(18,2) NOT NULL DEFAULT 0,
  branch_id uuid NULL,          -- attribution of the underlying invoice
  entry_date date NOT NULL,
  ref_id text,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_custledger_customer ON customer_ledger_entries (tenant_id, customer_id);

CREATE TABLE suppliers (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  phone text, address text,
  brand_or_category text, notes text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE supplier_transactions (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  supplier_id uuid NOT NULL REFERENCES suppliers(id),
  branch_id uuid NULL REFERENCES branches(id),   -- NULL = head office
  type text NOT NULL CHECK (type IN ('Bill','Payment','OpeningBalance')),
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  payment_mode text CHECK (payment_mode IN ('Cash','UPI','BankTransfer','Cheque','Credit')),
  reference_no text, txn_date date NOT NULL, notes text,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE supplier_ledger_entries (          -- append-only, same shape as customer ledger
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  supplier_id uuid NOT NULL REFERENCES suppliers(id),
  branch_id uuid NULL,
  entry_type text NOT NULL,     -- Bill|Payment|OpeningBalance|ReturnCredit
  debit numeric(18,2) NOT NULL DEFAULT 0,
  credit numeric(18,2) NOT NULL DEFAULT 0,
  entry_date date NOT NULL,
  ref_id text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE supplier_returns (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  return_number text NOT NULL,
  return_date date NOT NULL,
  supplier_id uuid NOT NULL,
  supplier_phone text, supplier_address text,
  total_qty int NOT NULL, total_amount numeric(18,2) NOT NULL,
  settlement_type text NOT NULL CHECK (settlement_type IN ('PayableDeduction','CashRefund','PendingCredit')),
  reason text, notes text,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, return_number)
);

CREATE TABLE supplier_return_items (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  return_id uuid NOT NULL REFERENCES supplier_returns(id),
  product_id uuid NULL,
  item_name text NOT NULL,
  qty int NOT NULL CHECK (qty > 0),
  purchase_price numeric(18,2) NOT NULL,
  return_rate numeric(18,2) NOT NULL,
  line_total numeric(18,2) NOT NULL,
  imei_numbers text[]
);
```

### 8.8 Expenses, liabilities, repairs

```sql
CREATE TABLE expenses (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NULL REFERENCES branches(id),   -- NULL = head office
  description text NOT NULL,
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  category text NOT NULL,                        -- Rent|Utilities|Salaries|Miscellaneous|<extensible>
  expense_date date NOT NULL,
  notes text,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_expenses_tenant_branch_date ON expenses (tenant_id, branch_id, expense_date);

CREATE TABLE liabilities (                      -- tenant-level
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  title text NOT NULL,
  type text NOT NULL CHECK (type IN ('Loan','CreditCard','Financing','Investment')),
  lender_name text,
  total_principal numeric(18,2) NOT NULL,
  due_date date, status text, notes text,
  created_at timestamptz NOT NULL DEFAULT now()
);
-- Current balance = total_principal - SUM(repayments); never a silently edited number.

CREATE TABLE liability_payments (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  liability_id uuid NOT NULL REFERENCES liabilities(id),
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  payment_mode text NOT NULL,
  payment_date date NOT NULL,
  notes text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE repair_jobs (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  job_number text NOT NULL,                     -- REP-<BR>-YYYYMMDD-XXXX
  customer_id uuid NULL,
  customer_name text, customer_phone text, customer_address text,  -- snapshots
  device_brand text, device_model text, device_type text,
  serial_number text, imei1 text, imei2 text,
  device_condition text, accessories_received text,
  problem_description text, customer_notes text,
  diagnosis text, technician_notes text, internal_notes text, technician text,
  estimated_cost numeric(18,2), approved_cost numeric(18,2),
  parts_cost numeric(18,2), labor_charge numeric(18,2),
  discount numeric(18,2), tax numeric(18,2), final_total numeric(18,2),
  advance_paid numeric(18,2), balance_due numeric(18,2),
  status text NOT NULL CHECK (status IN
    ('Received','Diagnosing','WaitingForApproval','WaitingForParts',
     'InRepair','ReadyForPickup','Delivered','Cancelled')),
  received_at timestamptz, expected_completion timestamptz,
  completed_at timestamptz, delivered_at timestamptz,
  warranty_until date,
  approval_state text, approval_timestamp timestamptz,
  cancellation_reason text,
  invoice_number text, sale_id uuid NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, job_number)
);
CREATE INDEX ix_repair_jobs_tenant_branch_status ON repair_jobs (tenant_id, branch_id, status);
CREATE INDEX ix_repair_jobs_tenant_branch_customer ON repair_jobs (tenant_id, branch_id, customer_id);

-- Chicken-and-Egg FK Resolution (sales.repair_job_id ↔ repair_jobs.sale_id):
-- Neither row can exist with a non-null pointer to the other if inserted simultaneously.
-- The repair-billing transaction resolves this cleanly in a deterministic two-step sequence:
-- 1. Insert the repair-billing sales row first with repair_job_id set to the existing repair job's ID.
-- 2. Update repair_jobs in the same transaction: SET sale_id = @saleId, invoice_number = @invoiceNumber WHERE id = @repairJobId.

CREATE TABLE repair_job_parts (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  repair_job_id uuid NOT NULL REFERENCES repair_jobs(id),
  product_id uuid NULL,
  part_name text NOT NULL,
  quantity int NOT NULL CHECK (quantity > 0),
  unit_cost numeric(18,2) NOT NULL,
  charge_amount numeric(18,2) NOT NULL,
  status text NOT NULL CHECK (status IN ('Reserved','Consumed','Returned')),
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE repair_job_status_history (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  repair_job_id uuid NOT NULL REFERENCES repair_jobs(id),
  from_status text, to_status text NOT NULL,
  notes text, changed_by uuid, changed_at timestamptz NOT NULL DEFAULT now()
);
```

Repair services are products with category `RepairService` (§8.4); defaults: SAC `998713`, GST 18%, duration 60 min, warranty 30 days.

### 8.9 Comprehensive Ledger Pattern & Nightly Reconciliation Sweep

Balances are **derived from append-only ledgers**; convenience columns are projections written in the same transaction. A nightly reconciliation job sweeps all transactional projections (not just stock) to assert mathematical consistency and alert on drift:

1. **Stock consistency:** `branch_inventory.stock_qty == SUM(inventory_movements.qty_delta)` per product per branch.
2. **Sale payment consistency:** `sales.amount_paid == SUM(customer_payments.amount)` per sale.
3. **Customer revenue consistency:** `customers.total_purchases == SUM(sales.grand_total WHERE status = 'Active')` per customer.
4. **Customer ledger balance:** Derived credit balance == `SUM(customer_ledger_entries.debit - customer_ledger_entries.credit)` per customer.
5. **Supplier payables balance:** Derived supplier payable == `SUM(supplier_ledger_entries.credit - supplier_ledger_entries.debit)` per supplier.

Any discrepancy triggers an alert to engineering via OpenTelemetry and logs a diagnostic problem report without interrupting ongoing transactions.

### 8.10 Platform tables

```sql
CREATE TABLE audit_log (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NULL,
  correlation_id text NULL,        -- correlation-id propagated from HTTP request / job context
  occurred_at timestamptz NOT NULL DEFAULT now(),
  user_id uuid,
  action text NOT NULL,            -- 'sale.voided', 'repair.status.changed', …
  entity_type text NOT NULL,
  entity_id text NOT NULL,
  changes jsonb
);
CREATE INDEX ix_audit_log_tenant_correlation ON audit_log (tenant_id, correlation_id) WHERE correlation_id IS NOT NULL;

CREATE TABLE import_batches (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NULL,
  source_type text NOT NULL,       -- SupplierInvoice|Excel|JsonBackup
  source_ref text NOT NULL,        -- file reference / batch tag
  status text NOT NULL,            -- Pending|Imported|Failed
  record_counts jsonb,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

-- Record-Level Deduplication for Incremental Imports:
-- Deduplication on whole-file hash (source_ref) prevents incremental re-imports when the desktop app
-- remains active as a pilot fallback (§17). A backup taken days later hashes differently and would duplicate records.
-- The import engine enforces record-level deduplication using natural business keys:
--   Sales: (tenant_id, original_invoice_number)
--   IMEIs: (tenant_id, imei)
--   Customers: (tenant_id, phone)
--   Purchases: (tenant_id, supplier_invoice_no, supplier_id, purchase_date)
-- Re-running migration on newer desktop backups imports newly created records and safely skips existing records.
```

Migrations: **EF Core migration bundles**, applied as an explicit deployment step (never `Migrate()` at startup in staging/production). A migration ledger satisfies the upgrade-without-data-loss requirement.

---

## 9. Access Scope & Enforcement

### 9.1 EF Core query filters (instance-scoped, evaluated per request)

```csharp
private readonly IAccessScope _scope;   // injected into the DbContext

modelBuilder.Entity<Sale>().HasQueryFilter(s =>
    s.TenantId == _scope.TenantId &&
    (_scope.AllBranches || _scope.BranchIds.Contains(s.BranchId)));
```

Every branch-scoped entity gets the same two-condition filter. Tenant-only entities (customers, products, suppliers) keep just the tenant condition.

### 9.2 PostgreSQL RLS & Connection Pooling — Critical Production Architecture

#### Architectural Mandate: Unified Transaction Scope for Reads and Writes

In PostgreSQL, `SET LOCAL` is valid **strictly within an active transaction block** (`BEGIN … COMMIT/ROLLBACK`). If plain read queries (`GET /customers`, dashboard metrics) run without an explicit transaction wrapper:
1. `SET LOCAL` cannot be executed outside a transaction block.
2. `current_setting('app.tenant_id', true)` evaluates to `NULL`.
3. Strict RLS policies (`tenant_id = current_setting('app.tenant_id')::uuid`) evaluate to `NULL` (false), causing plain read queries to **silently return zero rows** (visible bug).
4. Loosening the policy to `OR current_setting('app.tenant_id', true) IS NULL` to "make reads work" is **strictly forbidden**, as it causes uninitialized connections to **silently return all tenants' data across the database** (catastrophic cross-tenant breach).

**Architectural Decision: Every DbContext operation runs inside an explicit transaction context.**
- An ASP.NET Core middleware (`TenantTransactionMiddleware`) or EF Core execution strategy automatically begins an explicit transaction for every incoming database operation:
  - Read-only endpoints execute in a `ReadCommitted` transaction with `READ ONLY` mode.
  - Write and mutation endpoints execute within explicit `RepeatableRead` or `Serializable` transactions.
- An EF Core `DbTransactionInterceptor` (`TransactionStartedAsync`) immediately dispatches the session context parameters:
  ```sql
  SET LOCAL app.tenant_id = @tenantId;
  SET LOCAL app.branch_ids = @branchIds;
  SET LOCAL app.all_branches = @allBranches;
  ```
- **Benefits:**
  - `current_setting('app.tenant_id')` is guaranteed to be set on **every** read and write query.
  - Setting values automatically evaporate on `COMMIT` or `ROLLBACK`, guaranteeing zero context bleeding across pooled physical Npgsql connections.
  - RLS functions as an active, hardware-enforced second layer of defense on both reads and writes.

#### Written Rule: 100% RLS Coverage on Every Tenant Table

Row Level Security is NOT limited to branch-scoped tables. PII-bearing master tables (`customers`, `suppliers`, `users`) are the most sensitive assets in the system. A single raw-SQL query, projection query, or missed EF query filter must never leak customer phone numbers, GSTINs, or supplier financials.

**Rule:** Every table containing a `tenant_id` column MUST have RLS enabled and forced:
```sql
ALTER TABLE <table_name> ENABLE ROW LEVEL SECURITY;
ALTER TABLE <table_name> FORCE ROW LEVEL SECURITY;
```

**Policy Archetype A — Tenant-Only Tables (PII & Master Data):**
Applied to `customers`, `products`, `suppliers`, `liabilities`, `users`, `roles`, `settings`, `audit_log`, `import_batches`:
```sql
CREATE POLICY tenant_isolation_policy ON customers USING (
  tenant_id = current_setting('app.tenant_id')::uuid
);
```

**Policy Archetype B — Branch-Scoped Tables (Operational Data):**
Applied to `sales`, `sale_items`, `branch_inventory`, `purchases`, `inventory_imeis`, `inventory_movements`, `customer_payments`, `customer_ledger_entries`, `supplier_ledger_entries`, `expenses`, `repair_jobs`:
```sql
CREATE POLICY tenant_branch_isolation_policy ON sales USING (
  tenant_id = current_setting('app.tenant_id')::uuid
  AND (
    current_setting('app.all_branches', true) = 'true'
    OR branch_id = ANY (string_to_array(current_setting('app.branch_ids', true), ',')::uuid[])
  )
);
```

**Automated CI Enforcement Guard:**
`StoreOS.IntegrationTests` contains an automated schema-verification test that inspects `information_schema.columns`, `pg_class`, and `pg_policy`:
- Asserts that 100% of tables with a `tenant_id` column have `relrowsecurity = true` and at least one active policy.
- Asserts that no policy contains an `OR current_setting(...) IS NULL` bypass.
- Any pull request introducing a new table with `tenant_id` without its RLS migration bundle immediately fails the CI build.

#### Connection Pooling Safety Mandate

- **Native Npgsql Pooling:** The application connects directly using Npgsql's built-in connection pool. External poolers are omitted in standard deployments.
- **External Pooler Constraint (PgBouncer):** If PgBouncer or a cloud-managed proxy is deployed in front of PostgreSQL, it **MUST be configured strictly in session pooling mode (`pool_mode = session`)**, NEVER `transaction` or `statement` mode. `SET LOCAL` inside explicit transactions ensures isolation even in edge cases, but session mode ensures PostgreSQL session-level health remains intact.
- **Dual Defense Layers:** EF Core query filters (§9.1) execute in application memory on generated SQL; PostgreSQL RLS enforces row filtering at the database engine level.

### 9.3 Branch context on mutations

- Mutations carry an explicit branch context via the `X-Branch-Id` header (defaulted server-side to the user's default branch).
- The authorization behavior verifies the user holds the required permission **for that specific branch**, then the branch is stamped onto the record.
- There is no branchless sale: even an Owner must operate in a branch context.

### 9.4 Branch-scoped stock concurrency

```sql
UPDATE branch_inventory SET stock_qty = stock_qty - @qty
WHERE tenant_id = @tenant AND branch_id = @branch AND product_id = @product
  AND stock_qty >= @qty;
-- 0 rows affected → out of stock; transaction rolls back
```

Two terminals (same or different branches) selling the last unit(s) are serialized by this statement; the loser gets a clean `422 OutOfStock`. The tenant-wide active-IMEI unique index plus status checks make double-selling an IMEI impossible at any branch.

---

## 10. Critical Transactional Flows

### 10.1 Checkout

One application handler (`CheckoutHandler`), orchestrated across module boundaries via typed internal application services, executed within a single transaction (`Serializable` or `RepeatableRead`):

```text
BEGIN
  1. Validate command (FluentValidation): cart non-empty, qty > 0, payments valid,
     branch context ∈ user's accessible branches.
  2. Server-side pricing & pricing resolution:
     - Base retail price = branch_inventory.sell_price_override ?? products.sell_price.
     - Authorized overrides applied only with permission; otherwise client price is ignored.
     - Margin scheme: allowed only for UsedPhone items.
       * Cost basis = inventory_imeis.purchase_price (when IMEI selected),
                      else branch_inventory.cost_price > 0 ? branch_inventory.cost_price : products.purchase_price.
       * Hard Validation Guard: Cost basis MUST be strictly positive (> 0). If unseeded, 0, or negative,
         checkout immediately fails with 422 UnprocessableEntity ("Margin scheme requires a known positive cost basis").
         Silent fallback to 0 is strictly forbidden.
     - Compute tax via TaxEngine; GST rate from product (or tenant default setting).
  3. Credit validation (if payment_mode = 'Credit' or partial payment with balance > 0):
     - Compute live cross-branch outstanding debt across all branches.
     - If customer.credit_limit IS NOT NULL and live_debt + new_balance > credit_limit:
       block checkout unless user holds 'sales:credit:override' permission.
  4. Reserve invoice sequence number: upsert into invoice_sequences (§8.5) using
     configured series format (CA-confirmed FY consecutive or daily format).
  5. Stock & IMEI reservation via IInventoryStockService:
     - For each physical line: conditional stock decrement at sale's branch (§9.4).
     - For each IMEI line: verify IMEI status = 'InStock' FOR UPDATE, set status = 'Sold',
       stamp sale_id and sale_item_id.
     - (If invoice_type = 'Estimate', stock/IMEI decrements are completely bypassed).
  6. INSERT sales, sale_items (with complete historical price/tax/HSN snapshots).
     - Include nullable irn, irn_ack_no, irn_qr_data (ready for Phase 8).
     - If billing a repair: set sales.repair_job_id = @repairJobId.
  7. INSERT inventory_movements via IInventoryStockService:
     - Record Sale movement (negative qty, acquisition cost snapshot) per physical item.
  8. Payments & ledger via ICustomerLedgerService:
     - INSERT customer_payments rows; amount_paid = Σ payments; balance = max(0, total − paid).
     - If TaxInvoice and customer ≠ walk-in: INSERT customer_ledger_entries (Invoice debit,
       branch attribution).
     - UPDATE customers.total_purchases (projection).
  9. Repair billing link via IRepairBillingService (if repair_job_id present):
     - Complete step 2 of two-step FK resolution:
       UPDATE repair_jobs SET sale_id = @saleId, invoice_number = @invoiceNumber
       WHERE id = @repairJobId.
 10. INSERT audit_log (stamped with request correlation_id).
COMMIT
```

#### Concurrency & Serialization Failure Retry Wrapper

Under `Serializable` or `RepeatableRead` isolation, PostgreSQL's MVCC engine will throw error `40001 (serialization_failure)` or `40P01 (deadlock_detected)` when concurrent cashier transactions update the same branch inventory row or sequence counter. To prevent cashiers seeing spurious "Transaction Failed" errors at peak hours:
- `CheckoutHandler` is wrapped in an automated Polly/resilience retry pipeline keyed off the request's mandatory `Idempotency-Key` header.
- **Retry Policy:** Catches `NpgsqlException` where `SqlState IN ('40001', '40P01')`.
- **Backoff Strategy:** Up to 3 retries with exponential backoff and full jitter (initial delay 25ms, backoff factor 2.0, max delay 250ms, $\pm 10$ms jitter).
- **Idempotency Safety:** Because the client supplies an `Idempotency-Key`, the pipeline is guaranteed idempotent. Each retry executes in a fresh DbContext transaction, refreshing snapshot state and re-testing conditional stock availability.
- **Terminal Exhaustion:** If retries are exhausted under extreme sustained contention, returns `409 Conflict` (or `422 OutOfStock` if inventory was depleted by the competing transaction), accompanied by the request correlation-id.

### 10.2 Void / refund

- **Void** (permission `sales:void`): one transaction — set `status='Voided'`, invoke `IInventoryStockService` to insert reversing `inventory_movements`, restore `stock_qty`, transition IMEIs `Sold → InStock`, and invoke `ICustomerLedgerService` to insert reversing customer-ledger credit. Original rows are never deleted or mutated beyond status.
- **Refund**: mark refunded state (whole or line-level per business rules), return eligible stock via `IInventoryStockService`, transition IMEI `Sold → InStock`, and record the refund as its own payment/ledger transaction. Original sale preserved.

### 10.3 Subsequent customer payment

`POST /sales/{id}/payments` → validate `amount <= outstanding`, insert `customer_payments` row + invoke `ICustomerLedgerService` to record ledger credit, update the `amount_paid` projection. **Never mutate an existing payment row.** Idempotency-key and correlation-id protected.

### 10.4 Supplier return

One transaction: return header + lines, conditional stock decrement via `IInventoryStockService` (fail if insufficient at that branch), IMEIs → `ReturnedToSupplier` (linked via `return_id`), `supplier_ledger_entries` credit by settlement type (`PayableDeduction` / `CashRefund` / `PendingCredit`). Unique `return_number` per branch series.

### 10.5 Repair parts

Reserve → conditional stock decrement → `Consumed`; `Returned` → reversing movement via `IInventoryStockService`. Negative stock forbidden; consumption beyond stock fails atomically.

### 10.6 Branch transfer

```text
B01: TransferOut movement (stock_qty −= n), IMEIs → 'InTransit'
  → physical movement
B02: TransferIn movement (stock_qty += n), IMEI branch_id → B02, status → 'InStock'
```

- Never changes tenant-level total stock; reconciliation asserts balance.
- In-transit IMEIs are excluded from sale and cannot be re-stocked at origin (covered by the active-unique index).

### 10.7 Estimate to Tax Invoice conversion

`POST /pos/estimate/{id}/convert` enables cashiers to convert an accepted estimate directly into a binding tax invoice without re-entering the cart.
- **Mandatory Header:** `Idempotency-Key: <guid>` (required; duplicates return the cached `TaxInvoice` payload without re-decrementing stock or re-allocating sequence numbers).
- **Serialization Failure Wrapper:** Wrapped in the identical `40001`/`40P01` retry wrapper with jittered backoff as checkout.

```text
BEGIN
  1. Validate Idempotency-Key. If already processed, return existing invoice response.
  2. Load estimate by ID (verifying invoice_type = 'Estimate' and tenant/branch access).
  3. Verify current stock availability for all physical items and ensure specified IMEIs
     are currently 'InStock' at the branch.
  4. Validate customer credit limit if converting to an unpaid/credit sale.
  5. Reserve new Tax Invoice number from invoice_sequences.
  6. Invoke IInventoryStockService: decrement branch stock and transition IMEIs to 'Sold'.
  7. INSERT sales (invoice_type = 'TaxInvoice', invoice_number = new sequence,
     converted_from_estimate_id = estimate.id) and sale_items.
  8. INSERT inventory_movements (Sale) for all physical lines.
  9. Capture initial payments and record customer_ledger_entries if balance remains.
 10. Update original estimate record (status = 'Converted', linked_tax_invoice_id = sale.id).
 11. INSERT audit_log (with correlation-id).
COMMIT
```

### 10.8 Extended Nightly Reconciliation Sweep

Nightly background worker sweeps all projections across all active tenants and branches:

```text
For each tenant & branch:
  1. Assert: branch_inventory.stock_qty == SUM(inventory_movements.qty_delta)
  2. Assert: sales.amount_paid == SUM(customer_payments.amount) for each sale
  3. Assert: customers.total_purchases == SUM(sales.grand_total WHERE status = 'Active')
  4. Assert: Derived customer credit == SUM(customer_ledger_entries.debit - credit)
  5. Assert: Derived supplier payable == SUM(supplier_ledger_entries.credit - debit)
  On any mismatch:
    Emit alert via OpenTelemetry metric + create high-severity diagnostic entry.
```

---

## 11. REST API Surface

Base: `https://…/api`. Auth: `Authorization: Bearer <jwt>`. Request context headers: `X-Branch-Id: <guid>`, `X-Correlation-ID: <uuid>` (client may supply; server generates if omitted; always returned in response headers). All endpoints tenant-scoped by token; branch filtering applied by access scope.

| Area | Endpoints (representative) |
|---|---|
| Auth | `POST /auth/login`, `POST /auth/refresh` (HttpOnly cookie, family reuse detection), `POST /auth/logout`, `GET /auth/me` (incl. branches, roles, permissions, token_version) |
| Branches | `GET/POST/PATCH /branches`, `GET /branches/{id}`, user assignment endpoints (`user_branches`, `user_role_assignments`) |
| POS | `GET /pos/search?q=` (branch-scoped), `GET /pos/product/{id}` (incl. available IMEIs, branch sell price override), `POST /pos/checkout` (requires `Idempotency-Key`, 40001 retry-safe, validates stock/IMEI/credit limit), `POST /pos/estimate`, `POST /pos/estimate/{id}/convert` (requires `Idempotency-Key`, converts estimate to tax invoice, 40001 retry-safe) |
| Sales | `GET /sales` (all filters/sort/page: status, payment mode, type, paid/due, financed, category, amount range, dates, branch), `GET /sales/{id}` (incl. IRN/QR data), `POST /sales/{id}/payments` (requires `Idempotency-Key`), `POST /sales/{id}/void` (live DB permission re-check), `POST /sales/{id}/refund` (live DB permission re-check), `GET /sales/{id}/invoice.pdf` |
| Customers | `GET/POST/PATCH /customers` (incl. `credit_limit`, live cross-branch balance), `GET /customers/{id}/ledger` (per-invoice branch attribution), `GET /customers/{id}/sales`, `POST /customers/{id}/anonymize` (DPDP Act right to erasure handler) |
| Inventory | `GET/POST/PATCH /products`, `GET /inventory?branchIds=`, `GET /inventory/valuation?branchIds=` (routed to ReportingDbContext), `GET /inventory/low-stock`, `GET/POST /imeis`, `POST /inventory/adjustments` (live DB permission re-check) |
| Purchases | `POST /purchases` (stock-in with optional IMEIs), `GET /purchases`, `POST /imports/supplier-invoice` (deterministic mapping; ambiguous → manual mapping step; no AI) |
| Suppliers | `GET/POST/PATCH /suppliers`, `POST /suppliers/{id}/transactions`, `GET /suppliers/{id}/ledger?branchIds=`, `POST /supplier-returns`, `GET /supplier-returns/{id}/note.pdf` |
| Transfers | `POST /inventory/transfers` (out), `POST /inventory/transfers/{id}/receive` |
| Repairs | `GET/POST/PATCH /repair-services`, `GET/POST/PATCH /repair-jobs`, `POST /repair-jobs/{id}/status`, `POST /repair-jobs/{id}/parts`, `POST /repair-jobs/{id}/invoice` (deterministic two-step link), `GET /repair-jobs/{id}/ticket.pdf` |
| Expenses | `GET/POST/PATCH /expenses?from&to&branchIds=` |
| Liabilities | `GET/POST/PATCH /liabilities`, `POST /liabilities/{id}/repayments` |
| Reports | `GET /reports/dashboard?branchIds=`, `GET /reports/daily-sales?date&branchIds=`, `GET /reports/compare?date` (per-branch comparison), `GET /reports/inventory?branchIds=`, `GET /exports/gstr1?from&to` (grouped by GSTIN), `GET /exports/inventory?categories=&branchIds=` *(all routed to ReportingDbContext / read replica)* |
| Settings | `GET/PUT /settings` (tenant + branch scoped, series format: FY consecutive vs daily), `POST /tenants/me/logo` (PNG/JPEG/WebP <= 2MB, magic byte validation), finance providers CRUD |
| Platform | `GET /health/live`, `GET /health/ready`, `GET /tenant/manifest.webmanifest` (dynamic W3C manifest), `POST /tenant/export`, `POST /tenant/import` (record-level dedupe) |

- Query/report endpoints accept `branchIds` (multi) or `all`; branch users are restricted server-side to their visible set regardless of what they request.
- **Reporting Connection Seam:** Reports and exports execute exclusively via `ReportingDbContext` (`QueryTrackingBehavior.NoTracking`), isolating heavy analytical aggregate queries from the primary checkout database.
- **OpenAPI → TypeScript client:** NSwag generates `frontend/src/app/core/api/` at build; CI regenerates and fails on drift. No hand-written API calls.
- **Rate limiting:** `POST /auth/login` 5/min/IP; global 200 req/min/user.

---

## 12. Frontend (Angular)

### 12.1 First-Run Shop Onboarding & Dynamic White-Labeling

The application operates as a **strict white-label solution**. The platform name `StoreOS` is strictly an internal architectural identifier and is completely invisible to cashiers, store managers, and retail customers.

1. **Subdomain-Per-Tenant Architecture & Ingress Infrastructure:**
   - **Wildcard DNS & TLS:** Wildcard DNS record `*.pos.storeos.io` routes to the edge CDN/load balancer. TLS certificates are managed automatically via Let's Encrypt / Cloudflare Wildcard SSL (`*.pos.storeos.io`).
   - **Origin Isolation for PWAs:** Each tenant operates under its own distinct subdomain origin (e.g., `https://apex.pos.storeos.io`). Because browser security sandboxes (Origin, IndexedDB, Service Worker scope, CacheStorage) are strictly partitioned per origin, tenants never share local client storage or PWA service worker contexts.
   - **Dynamic W3C Web Manifest (`/api/tenant/manifest.webmanifest`):**
     * The Angular `index.html` references `<link rel="manifest" href="/api/tenant/manifest.webmanifest">`.
     * The API resolves the tenant from the HTTP `Host` header (`apex.pos.storeos.io`), retrieves custom branding, and dynamically generates the W3C manifest JSON with:
       ```json
       {
         "name": "Apex Mobiles POS",
         "short_name": "Apex POS",
         "start_url": "/",
         "display": "standalone",
         "theme_color": "#1e293b",
         "background_color": "#0f172a",
         "icons": [{ "src": "/api/tenant/logo.png?size=512", "sizes": "512x512", "type": "image/png" }]
       }
       ```
     * When a cashier on a tablet or desktop clicks "Install App", the browser installs the application natively under the shop's name and icon (e.g., *"Apex Mobiles POS"*), with zero vendor trace.
   - **Universal Entrypoint & Subdomain Routing:**
     * `https://pos.storeos.io` serves as a universal login portal. Upon email entry or authentication, users are redirected to their tenant's specific subdomain (`https://<tenant>.pos.storeos.io`).

2. **First-Run Onboarding Modal / Wizard:**
   - When an owner provisions a tenant or logs in for the first time, the onboarding flow immediately prompts:
     * **"What is your Shop / Business Name?"** (e.g., *"Apex Mobiles"*).
     * Optional brand details: logo upload, GSTIN, store address, phone numbers, email, and thermal receipt footer message.
   - Once submitted, the system saves `tenants.name` and initializes the branch settings.

3. **Tenant Logo Upload & Asset Security:**
   - **Endpoint:** `POST /tenants/me/logo` (restricted to tenant `settings:manage` permission).
   - **Validation Rules:**
     * Allowed MIME types: strictly `image/png`, `image/jpeg`, `image/webp`. SVG format is **strictly forbidden** (or sanitized with an aggressive SVG sanitizer) to prevent stored Cross-Site Scripting (XSS) vectors.
     * Maximum file size: **2 MB**.
     * Binary magic-number inspection: verifies file header bytes match the declared image format.
   - **Storage & Processing:** Uploaded images are normalized/resized via ImageSharp (max 512×512 icon, max 1024×256 invoice header), stripped of EXIF metadata, and saved to private object storage under `tenants/{tenant_id}/branding/logo_{hash}.webp`. Served via CDN edge cache with immutable cache headers.

4. **Dynamic Shop Identity Across the Entire Application:**
   - **Browser Tab `<title>`:** Driven dynamically by Angular's `Title` service: `{{ activeBranch.name }} | {{ currentTenant.name }}` (e.g., `Main Branch | Apex Mobiles`).
   - **Top Navigation Bar:** Displays the shop's custom brand name and logo front-and-center, accompanied by the active branch switcher.
   - **Login & Lock Screen:** Dynamically renders the tenant's brand name and logo (resolved via subdomain or user account context), never vendor branding.
   - **Customer Invoices, Receipts, & PDFs:** QuestPDF templates format the header strictly using the shop's trade name, logo, address, and GSTIN.
   - **SMS / WhatsApp Notifications:** Messages originate exclusively under the shop's identity.

### 12.2 Structure & Token Storage Architecture

```text
src/app/
├── core/        # bootstrap, interceptors (auth, error, idempotency, correlation-id, X-Branch-Id),
│                # auth signal store, generated API client, guards, permission directives
├── shared/      # design system: pz-table, pz-form-field, pz-dialog, money pipe (INR/en-IN),
│                # date pipe, empty/loading/error states, toast service
└── features/
    ├── auth/  pos/  sales-history/  customers/  inventory/  purchases/
    ├── transfers/  suppliers/  repairs/  expenses/  liabilities/  reports/  settings/
    └── admin/ (branches, users, roles)
```

#### Token Storage & Auth Flow (In-Memory Access Token + HttpOnly Cookie)

Storing JWT access tokens in browser `localStorage` or `sessionStorage` exposes financial and PII data to token exfiltration via Cross-Site Scripting (XSS). StoreOS enforces a secure **BFF Token Architecture**:

1. **Short-Lived Access Token (15-min JWT):**
   - Kept **strictly in memory** inside Angular's `AuthSignalStore` (a private signal state service).
   - **Never persisted** to `localStorage`, `sessionStorage`, or `IndexedDB`.
   - If the browser tab is refreshed or closed, the in-memory token is immediately destroyed.
2. **Long-Lived Rotating Refresh Token (30 Days):**
   - Issued in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie (`storeos_refresh_token`, path `/api/auth`).
   - Browser JavaScript has zero read/write access to this cookie, eliminating XSS token theft.
3. **Silent Refresh on Application Bootstrap:**
   - On page load or tab refresh, Angular's `APP_INITIALIZER` dispatches a silent `POST /api/auth/refresh` request.
   - The browser automatically attaches the HttpOnly cookie. The endpoint responds with a fresh 15-minute access token and current user profile, re-hydrating the `AuthSignalStore` without requiring user re-login.
4. **Auth Interceptor Workflow:**
   - The Angular `auth.interceptor.ts` injects `Authorization: Bearer <memory_token>` from `AuthSignalStore`.
   - On `401 Unauthorized`:
     * Buffers all outgoing concurrent HTTP requests.
     * Fires a single `POST /api/auth/refresh` request.
     * On success: stores the new access token in `AuthSignalStore` and replays all buffered requests with the new token.
     * On failure (refresh token expired, revoked, or theft detected): clears all memory state and redirects cashier to `/login`.
5. **CSRF & Cookie Protection:**
   - The refresh cookie specifies `SameSite=Strict`, blocking cross-site transmission.
   - State-changing auth requests require the custom header `X-Requested-With: XMLHttpRequest` and an anti-CSRF token verified on the server.

- **State:** Angular signals for cart, current user, permissions, active branch; RxJS `toSignal` + `switchMap` for debounced search.
- **Forms:** reactive forms; server validation errors mapped back to fields.
- **Permissions:** structural directive `*pzCan="'sales:void'"` hides affordances — the server remains the enforcer.
- **Interceptors:** `correlation-id.interceptor.ts` automatically attaches `X-Correlation-ID` to outgoing requests and captures response correlation IDs to attach to error toasts and problem-details modals.
- **PWA:** service worker caches the app shell; the cart persists in IndexedDB so a network blip mid-billing doesn't lose it; checkout requires connectivity.

### 12.3 Branch UX

- **Branch switcher** in the top bar — visible only when the user has multiple branches or all-branch access. Switching sets `X-Branch-Id` via the interceptor; no re-login (the JWT already carries the branch set).
- Branch badge on sales history, repairs, and invoices for cross-branch users.
- Owner dashboard: branch selector, per-branch comparison table (revenue, invoice count, profit, active repairs), consolidated totals.
- Reports: multi-select branch filter; single-branch users see none of this UI.

### 12.4 POS screen — keyboard-first

- Global search auto-focused; barcode-scanner wedge handling: buffer rapid keystrokes, fire lookup on Enter when input arrives < 50 ms/key and ends with Enter (distinguishes scanner from typing); camera scanning (`@zxing/browser`) on mobile.
- Hotkeys: `F2` focus search · `+`/`–` qty · `Del` remove line · `F4` payment · `F8` park cart.
- Stock badges (in/low/out) per branch; customer typeahead by name/phone with live cross-branch credit balance display; credit limit warning dialog with manager override option when limits are exceeded.
- Estimate workflow: action in cart to save as Estimate; dedicated "Convert to Invoice" action on estimate details view without rebuilding cart.
- Payment split for partial payments; finance dialog with remembered providers (normalized, deduplicated case-insensitively; removal never touches history).
- Printing: fetch server PDF (QuestPDF layout reserves space for Phase 8 e-invoicing IRN and B2B signed QR code) → browser print dialog. **Printing failure never rolls back a completed sale** — show a "printing failed, sale saved" toast with a reprint action and correlation-id for support.

---

## 13. Security Hardening

```text
HTTPS everywhere                       Parameterized queries only (EF Core)
In-memory JWT + HttpOnly refresh       Rate limiting on auth + global
Tenant isolation: query filters + RLS  Branch isolation: scoped filters + RLS
100% RLS coverage on all tenant tables Unified transaction scope on all queries
Server-recomputed prices/totals        Idempotency on all money & stock mutations
Append-only audit + ledger tables      Permission-gated sensitive data (cost/profit)
Live DB check for high-impact actions  Token family theft/reuse detection
Secrets in cloud secret manager        Dependency + image scanning in CI
Input validation on every endpoint     Cross-branch access tests (horizontal)
Correlation-ID on all requests/audits  Strict session-mode DB pooling
DPDP Act compliant PII anonymization   WORM object lock on encrypted backups
```

### 13.1 Dynamic CORS Architecture

Because the frontend is hosted across dynamic tenant subdomains (`https://<tenant>.pos.storeos.io`) and static CDN origins while the API runs on a dedicated domain, ASP.NET Core applies a strict CORS policy:
- **Origin Validation:** Evaluated via a dynamic origin matcher:
  * Production: `^https://([a-z0-9-]+)\.pos\.storeos\.io$` and `^https://pos\.storeos\.io$`
  * Local Dev: `http://localhost:4200`
- **Allowed Methods:** `GET, POST, PUT, PATCH, DELETE, OPTIONS`
- **Allowed Headers:** `Content-Type, Authorization, X-Branch-Id, X-Correlation-ID, Idempotency-Key, X-Requested-With`
- **Exposed Headers:** `X-Correlation-ID, Idempotency-Key, Content-Disposition`
- **Credentials:** `AllowCredentials: true` is strictly enabled to permit transmission of the `SameSite=Strict` refresh token cookie during auth flows.
- **Preflight Caching:** `MaxAge = TimeSpan.FromHours(1)` to minimize OPTIONS round-trips on POS terminals.

### 13.2 Data Privacy & India DPDP Act 2023 Compliance

StoreOS centralizes retail customer PII (names, phone numbers, addresses, GSTINs) into a multi-tenant cloud repository. Compliance with India's Digital Personal Data Protection (DPDP) Act 2023 is designed into the core data model:

1. **Fiduciary vs. Processor Role:**
   - **Retail Tenant:** The **Data Fiduciary**, maintaining the primary relationship with the retail consumer and determining business purposes for collecting data.
   - **StoreOS:** The **Data Processor**, processing customer data strictly on behalf of the tenant and subject to tenant instructions.
2. **Statutory Retention vs. Right to Erasure / Anonymization:**
   - Under Section 36 of the Central Goods and Services Tax (CGST) Act 2017, registered businesses are **statutorily mandated to retain all books of accounts and tax invoices for a minimum of 72 months (6 years)** from the due date of filing the annual return.
   - **Erasure / Anonymization Protocol (`POST /customers/{id}/anonymize`):**
     * When a customer exercises their right to data erasure under the DPDP Act, the system performs an in-place irreversible anonymization rather than deleting fiscal invoice records:
       - `customers` row: `name = 'Anonymized Customer'`, `phone = NULL`, `gstin = NULL`.
       - Ledger records: linked customer name snapshots replaced with `'Anonymized Customer'`.
       - `sales` / `sale_items`: immutable invoice numbers, amounts, and GST splits are preserved for statutory tax audit integrity, with customer contact fields scrubbed.
     * Generates an audit log entry (`customer.anonymized`) stamped with the correlation-id and operator user ID.
3. **Data Minimization at POS:**
   - In accordance with Indian GST laws, cash retail transactions under ₹50,000 do not require customer identification. The POS screen defaults to `Walk-in Customer` and allows cashiers to bill without forcing collection of phone numbers.
4. **Notice & Consent on Digital Receipts:**
   - Invoices sent via SMS or WhatsApp include a standard statutory notice: *"Digital invoice issued by [Shop Name]. To view privacy preferences or manage your information, visit [tenant.pos.storeos.io/privacy]."*

### 13.3 Authorization & Stale Permission Mitigation

- **Two-Tiered Authorization:**
  * Standard operations: Fast authorization via JWT claims validated against the user's current `token_version` cached in memory.
  * **High-Impact Operations:** Sensitive endpoints (`POST /sales/{id}/void`, `POST /sales/{id}/refund`, `POST /inventory/adjustments`, `PUT /settings`, `PATCH /branches/{id}`) **bypass JWT token claims** and execute a live, direct database query against `user_role_assignments` and `role_permissions` inside the transaction. If an employee's void authority is revoked, their ability to void sales is cut off with zero latency.
- **Refresh Token Family & Theft Detection:**
  * Refresh tokens are linked via `family_id`. Presenting an already-rotated token triggers immediate theft mitigation: the entire token family is revoked, `token_version` is incremented, all active sessions for the user are invalidated, and an alert is dispatched.

- Disabled tenant blocks login; deactivated branch blocks new operations but never deletes history.
- Renderer/client can never execute arbitrary database operations — only the typed API.

---

## 14. DevOps

### 14.1 Docker & Database Pooling

- Multi-stage `Dockerfile.api` (SDK build → ASP.NET runtime, non-root) and `Dockerfile.web` (node build → nginx).
- `deploy/docker-compose.yml` for dev: `api`, `postgres`, `minio`, `otel-collector`.
- **PostgreSQL Connection Pooling Rule:** Applications connect using Npgsql's built-in pool. If PgBouncer or an external cloud connection pooler is deployed, it **MUST be configured strictly in session mode (`pool_mode = session`)**. Transaction pooling mode is prohibited due to PostgreSQL RLS session-variable bleeding across tenants.

### 14.2 CI/CD (GitHub Actions)

```text
push/PR → build (dotnet & ng) → unit tests → integration tests (Testcontainers, RLS on)
        → EF migration bundle artifact → Trivy scan (api + web images)
        → docker build/push (tag = sha) → deploy STAGING
        → run migration bundle against staging
        → smoke tests (health, login, create+read a sale, cross-branch denial)
        → [manual approval gate] → deploy PROD → migrations → smoke → auto-rollback on failure
```

Environments: `dev` (compose), `staging`, `production` (GitHub Environments with reviewers + secrets). Secrets live in the cloud secret manager — never in the repo; dev via `dotnet user-secrets`.

### 14.3 Backups, DR & Encryption Architecture

- **Platform-Level PITR:** Managed PostgreSQL Continuous WAL archiving / Point-In-Time Recovery (7–35 days retention).
- **Nightly Logical Dumps (`pg_dump`):** Nightly automated schema + data dumps per environment.
- **Backup Encryption at Rest (Mandatory):**
  * All database dump files and per-tenant JSON exports MUST be encrypted at rest using AES-256-GCM / AWS KMS SSE-KMS (or Azure Key Vault / GCP KMS customer-managed encryption keys). Plaintext database dumps in object storage are strictly prohibited.
  * Encrypted streaming via GPG/AES pipeline before upload, or enforced via S3 bucket policy requiring `x-amz-server-side-encryption: aws:kms`.
- **Bucket Access Scoping & IAM Least Privilege:**
  * Backup object storage buckets are completely private; Block Public Access is enforced at the cloud account root level.
  * Dedicated backup runner service principal / IAM role granted **write-only permissions (`s3:PutObject`)** on backup prefixes.
  * `s3:DeleteObject` and bucket mutation permissions are strictly denied to all application runtime identities.
  * **WORM Object Lock (Compliance Mode):** Buckets enforce S3 Object Lock (Compliance Mode) with a 30-day immutability retention period, preventing ransomware tampering, insider threats, or accidental deletion.
- **Monthly Restore Drill in CI/Staging:** Restore the latest dump into a scratch DB and run automated reconciliation assertions (row counts, ledger sums, `stock_qty` vs movements). Alert on failure.
- **Per-Tenant JSON Export:** Automated and on-demand tenant data export to private encrypted object storage.

### 14.4 IaC (Terraform / Bicep)

Terraform/Bicep modules provision:
- Multi-AZ resource group & virtual network.
- Managed PostgreSQL (Primary Writer) + Read Replica connection provisioning for `ReportingDbContext`.
- Encrypted object storage buckets (backup WORM bucket + tenant media bucket) with KMS keys and lifecycle rules.
- Container host (Azure Container Apps / AWS ECS / Cloud Run) with non-root Docker execution.
- Secret manager & key vault for DB credentials and JWT signing keys.
- Edge CDN with Wildcard DNS/TLS certificate (`*.pos.storeos.io`) and dynamic CORS configuration.
- OpenTelemetry collector and monitoring ingestion. Staging and production differ only by variables.

---

## 15. Observability & Supportability

- **End-to-End Correlation-ID Tracking:**
  - `X-Correlation-ID` header accepted from clients or generated server-side on first entry.
  - Echoed in all HTTP responses and RFC 7807 `ProblemDetails` error payloads (`extensions["correlationId"]`).
  - Automatically pushed into Serilog `LogContext` for structured JSON log enrichment across all log entries.
  - Attached to OpenTelemetry distributed trace activities (`Activity.Current`) and propagated to downstream HTTP calls and Hangfire background jobs.
  - Persisted in `audit_log.correlation_id` to establish an unbroken link between user business actions and technical log telemetry.
  - Cashiers seeing error dialogs can copy the Correlation ID to support; support can filter logs/traces by this ID to instantly view the entire execution flow.
- **OpenTelemetry:** traces (HTTP + EF Core + Hangfire), metrics (checkout latency, stock-conflict rate, payment endpoints), structured logs (Serilog → OTLP).
- **Health checks:** `/health/live` (process), `/health/ready` (DB, storage, jobs) — used by smoke tests and orchestrator probes.
- **Error tracking with tenant/user context:** PII-minimal, indexed by correlation ID and tenant ID.
- **Business alerts:** nightly ledger reconciliation drift (stock, payments, revenue, credit, payables), failed backup dumps, checkout p95 > 1 s.

---

## 16. Testing Strategy

| Layer | Tooling | Covers |
|---|---|---|
| Domain unit | xUnit | GST standard/zero/custom/multi-line/rounding; margin scheme incl. margin ≤ 0 and non-used-phone rejection; discount factor; EMI zero/positive interest/invalid inputs; valuation math |
| Module integration | xUnit + Testcontainers PostgreSQL (RLS enabled) | Checkout paths (full/partial/credit/finance/gift/discount); estimate ≠ sale; void/refund preserve history; payments as separate rows; stock/IMEI lifecycle; supplier return atomicity; repair lifecycle + parts; report totals reconcile |
| Concurrency | xUnit parallel tasks | Two checkouts of last unit → exactly one succeeds; parallel invoice numbering across branches → unique; duplicate IMEI → rejected; parallel payments → no overpay |
| Access control | integration | Cashier at B01 cannot read/mutate B02 (RLS-proven); cross-branch visibility for tenant-scoped roles; sensitive-data masking for viewers without cost permission |
| API contract | OpenAPI diff in CI | Generated client never drifts |
| E2E (later phases) | Playwright | POS end-to-end, credit → later payment, repair ticket flow, branch switcher |
| Restore drills | CI/staging | Backup restore + reconciliation |

**Golden rule: every product invariant gets at least one automated test**, including:

| Invariant | Test |
|---|---|
| 100% RLS table coverage | Automated CI schema test: asserts all tables with `tenant_id` have `relrowsecurity = true` and no NULL fallback |
| No double-selling a physical unit (any branch) | IMEI unique index + status check |
| No sale above available stock (per branch) | Conditional update test |
| Serialization failure recovery (`40001`/`40P01`) | Parallel contention test: assert Polly retry wrapper catches serialization failures and succeeds on backoff |
| Margin scheme zero/unseeded cost rejection | Unit & integration tests: assert $P \le 0$ rejects sale with 422, zero fallback prohibited |
| Refresh token replay & theft detection | Security test: assert presenting a rotated token revokes entire token family and forces re-login |
| Sensitive actions bypass stale JWT claims | Authorization test: assert revoking `sales:void` immediately blocks void on active JWT via live DB check |
| DPDP customer erasure preserves GST compliance | Privacy test: assert customer PII anonymized while invoice numbers, amounts, and GST totals remain intact |
| Repair services never count as stock | Reconciliation: no `branch_inventory` rows for services; valuation excludes them |
| Historical invoices survive master-data changes | Snapshot columns asserted |
| Payments are transactions, not overwrites | Payment rows never mutated |
| Voids/refunds preserve history | Status + original rows intact |
| Supplier returns remove sellable stock; returned IMEIs unavailable | Status assertions |
| Estimates don't inflate revenue/GST/debt | Report totals test |
| Reports reconcile with transactions (and consolidated = Σ branch reports) | Reconciliation test over seeded multi-branch data |
| Failed transactions are atomic | Rollback assertions |
| Backups never silently corrupt data | Restore drill + validation |
| Consolidated report == sum of branch reports | Multi-branch seeded data test |
| Transfer preserves tenant total stock | Transfer in/out balanced; IMEI uniqueness maintained |

---

## 17. Data Migration from the Desktop App

The desktop product's JSON backup is the bridge.

1. **Tool:** `pz-migrate` (CLI or admin-only Platform API). Input: desktop JSON backup.
2. **Steps:** detect schema/version (reject unknown) → validate structure → map to the cloud schema (assign tenant, create a default "Main Branch", stamp every record with `branch_id`, remap ids, seed users/roles) → **dry-run mode** producing a reconciliation report → **record-level deduplicated import** in a single transaction.
3. **Record-level deduplication & phone edit reconciliation for incremental sync:**
   During the pilot phase (§17.4), the desktop app remains active as a fallback. An updated backup taken days or weeks later will have a different file hash. Instead of failing on file-hash collisions, `pz-migrate` dedupes per-record based on immutable keys and natural business keys:
   - Sales: `(tenant_id, original_invoice_number)`
   - IMEIs: `(tenant_id, imei)`
   - Purchases: `(tenant_id, supplier_invoice_no, supplier_id, purchase_date)`
   - **Customers Two-Tier Deduplication:**
     * *Primary Match:* `(tenant_id, desktop_id)`. The migration engine maps the desktop SQLite customer primary key to `customers.desktop_id`. If a customer's phone number was edited on the desktop app between two pilot syncs, the match succeeds via `desktop_id`, updating the phone in cloud rather than creating a duplicate customer.
     * *Secondary Match:* `(tenant_id, phone)` (fallback when `desktop_id` is missing).
   - **Conflict Logging & Manual Reconciliation Path:**
     * If an incremental sync detects a conflict (e.g. `desktop_id` matches an existing cloud customer but the phone number matches a different existing customer), `pz-migrate` logs the divergence to `migration_conflicts.json` and skips the record without aborting the batch.
     * A dedicated CLI tool `pz-migrate reconcile-customers --tenant=<id> [--dry-run]` lists phone divergences and provides an interactive command to merge duplicated customer records and consolidate their ledger histories before final pilot cutover.
4. **Reconciliation report (mandatory):** per-table record counts, Σ grand totals, Σ payments, Σ stock qty, active credit balances — source vs. imported. Mismatch = failed migration; nothing is committed.
5. **Rollout:** pilot with 2–3 real shop backups → staging import → owner verifies the daily-sales report matches the desktop report for a known date → cut over; keep the desktop app read-only as a fallback for 2–4 weeks.

Multi-branch tenants branch out *after* migration (initial data lands in the default branch; branch transfers then redistribute stock).

---

## 18. Implementation Roadmap

| Phase | Scope | Exit criteria |
|---|---|---|
| **0 — Foundation** (weeks 1–3) | Repo, Docker, CI, migrations, Identity (login/JWT/refresh/RBAC), **branches + scoped role assignments + `X-Branch-Id` middleware + branch-aware RLS**, settings, health, correlation-id pipeline, observability | A user of a seeded tenant logs in; RLS blocks cross-tenant *and* cross-branch reads (tests prove both) |
| **1 — Catalog & Inventory** (weeks 4–6) | Products catalog, branch inventory (with sell price overrides), IMEIs, stock-in via purchases, movements ledger, valuation, low/out-of-stock, adjustments | Inventory acceptance block; IMEI tenant-wide uniqueness proven under concurrency |
| **2 — POS & Sales** (weeks 7–11) | Search/scan, cart, GST/margin/discount engine, checkout transaction (via internal module services), per-branch invoice numbering (CA-confirmed FY series), payments (cash/UPI/card/credit), estimates + conversion endpoint, invoice PDF (with IRN/QR slots), sales history + detail | POS + Sales acceptance blocks; concurrency tests green; estimate-to-invoice conversion verified |
| **3 — Credit lifecycle** (weeks 12–13) | Customer master/ledger, customer credit limits + cross-branch balance display, subsequent payments, void/refund with history preservation | Payment/void/refund invariants tested; credit limit enforcement verified |
| **4 — Suppliers** (weeks 14–16) | Supplier master, transactions, payables (per-branch + consolidated), supplier returns + IMEI return flow | Supplier acceptance block |
| **5 — Repairs** (weeks 17–19) | Service catalogue, job tickets, status history, parts consumption, repair billing (two-step FK link), warranty, feature flag abstraction | Repairs acceptance block; no circular FK insert locks |
| **6 — Finance & reporting** (weeks 20–23) | Expenses, liabilities + repayments, dashboards (per-branch + consolidated + comparison), daily sales audit, inventory & GSTR-1 exports grouped by GSTIN, extended nightly reconciliation worker | Reports reconcile with transactions; nightly sweep passes |
| **7 — Migration & hardening** (weeks 24–26) | `pz-migrate` (record-level incremental dedupe), JSON export/import, rate limits, pen-test pass, load test (100 concurrent POS users), restore drills | Two real desktop backups migrated incrementally & reconciled |
| **8 — Roadmap / Deferred** | **GST E-invoicing & IRP Integration (IRN & signed QR code via GSP/IRP for PANs crossing ₹5cr turnover)**, PAN aggregate turnover tracking & compliance alerts, branch transfer workflow, offline PWA enhancements, mobile app, SignalR live dashboards, full offline sync | Explicitly out of the initial release |

---

## 19. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Shops lose internet → billing stops | Cloud-first is a confirmed product decision; PWA keeps the cart; recommend 4G backup at the shop; full offline sync only if demanded |
| Numbering/stock races under multi-terminal load | Conditional updates + unique indexes + concurrency test suite from Phase 2 |
| Serialization failure (`40001`) under peak concurrent checkout | Automated Polly retry pipeline with exponential jittered backoff keyed on `Idempotency-Key` for checkout and estimate conversion |
| Cross-branch data leakage | Two independent layers (query filters + RLS) + horizontal access tests in CI |
| RLS + connection pool tenant data leak | Unified transaction scope on all reads/writes; `SET LOCAL` within all transactions; 100% table RLS coverage; strict `pool_mode = session` if external poolers deployed |
| GST Rule 46(b) serial numbering challenge | Configurable sequence generator supporting `<BRANCH>/<FY>/<NNNNNN>` (consecutive across FY) vs daily; mandatory CA signoff before go-live |
| E-invoicing ₹5cr threshold per-PAN | Turnover aggregates across all branches under tenant's PAN; pre-wired schema (`irn`, `irn_ack_no`, `irn_qr_data`), PDF template ready, Phase 8 roadmap |
| Cross-module writes breaking architecture | Internal typed application services (`IInventoryStockService`, `ICustomerLedgerService`) orchestrated within ambient EF Core transaction |
| Margin scheme cost drift or silent zero fallback | Domain guard: acquisition cost pulled from `inventory_imeis.purchase_price` or positive branch cost basis ($P > 0$); unseeded/zero cost rejected with 422 |
| Stale permissions in 15-minute JWT window | Live direct DB query inside transaction for sensitive actions (`sales:void`, `sales:refund`, `settings:manage`); user `token_version` check on all requests |
| Refresh token theft / replay | Token family lineage tracking; replaying an already-rotated token revokes entire token family and terminates all sessions |
| Unbounded cross-branch customer debt | Consolidated live balance displayed at POS checkout; configurable customer credit limit with warning/override gate |
| Reporting aggregates contending with checkout hot path | Architecture seam: `ReportingDbContext` with distinct connection string (`ConnectionStrings:Reporting`), ready for zero-code PostgreSQL read replica routing |
| Backup dump data exposure | Mandatory SSE-KMS encryption at rest, private bucket with write-only IAM runner, 30-day WORM Object Lock |
| DPDP Act compliance on centralized customer PII | Data processor role definition; anonymization endpoint scrubs customer PII while preserving 72-month immutable GST invoice records |
| Cloud cost sensitivity for small shops | Tenant density on shared DB; small container + basic PostgreSQL per environment |
| Incremental migration failures or phone edit dupes | Two-tier deduplication (`desktop_id` primary, `phone` fallback), conflict logging, and `pz-migrate reconcile-customers` CLI tool |
| Print/format regressions vs. desktop | QuestPDF templates data-driven; side-by-side comparison during the Phase 2 pilot |
| GST rule changes | Tax engine isolated + rates via settings; unit tests updated in one place |

---

## 20. Out of Scope for Initial Release

- SaaS billing/subscription payments (tenant activation flag only).
- Microservices, event streaming, multi-region.
- Full offline POS with conflict resolution.
- AI-based supplier invoice parsing (explicitly forbidden as a hidden dependency — deterministic mapping or manual entry only).

---

## 21. Definition of Done

The implementation is complete when **every checkbox in the product requirements' acceptance section passes**, plus:

- [ ] All product invariants covered by automated tests (including cross-branch and consolidation invariants)
- [ ] 100% of tables with `tenant_id` verified protected by RLS via automated CI schema assertions (no NULL fallback)
- [ ] Unified transaction scope verified: reads and writes both execute with `SET LOCAL app.tenant_id` active
- [ ] Serialization failure (`40001`/`40P01`) retry wrapper verified under concurrent checkout and estimate conversion load
- [ ] Margin scheme zero/unseeded cost rejection ($P \le 0$) verified with 422 validation
- [ ] Refresh token reuse detection revokes entire token family on replay (tested)
- [ ] Live DB permission re-check verified for sensitive operations (`sales:void`, `sales:refund`)
- [ ] Subdomain-per-tenant routing (`*.pos.storeos.io`) and dynamic W3C web manifest verified
- [ ] Backup artifacts verified encrypted at rest with 30-day WORM retention lock
- [ ] Customer PII anonymization verified to preserve historical GST invoice numbers and tax splits
- [ ] RLS + query filters verified against cross-tenant **and** cross-branch access
- [ ] Idempotency proven on checkout, payment replay, and estimate conversion (scoped by endpoint in PK)
- [ ] Request Correlation-ID (`X-Correlation-ID`) verified across HTTP headers, Serilog logs, OpenTelemetry traces, and `audit_log`
- [ ] Branch-scoped invoice numbering proven collision-free under parallel load (with CA-confirmed FY sequence format)
- [ ] Estimate-to-Invoice conversion (`POST /pos/estimate/{id}/convert`) verified end-to-end
- [ ] Customer credit limit and live cross-branch balance display verified at POS checkout
- [ ] Two-step repair billing insert sequence verified free of circular FK deadlocks
- [ ] Extended nightly reconciliation worker validates stock, payments, customer revenue, credit balances, and supplier payables
- [ ] Incremental desktop backup migration verified with two-tier deduplication (`desktop_id` + phone)
- [ ] Consolidated reports equal the sum of branch reports (tested)
- [ ] CI/CD runs the full pipeline including staging smoke + production approval gate
- [ ] Backup restore drill has executed successfully at least once
- [ ] A real desktop backup has been migrated and reconciled in staging (including incremental sync)
- [ ] OpenAPI-generated TypeScript client is the only frontend API surface
- [ ] Printing failure never rolls back a completed sale

---

## Appendix A — Golden Rules

> **Backend owns business logic.**
> **Database protects data integrity.**
> **Frontend handles UX, not authority.**
> **Every money/inventory operation is transactional.**
> **Every tenant is isolated; every branch-scoped user is confined to their branches.**
> **Every important mutation is auditable.**
> **Keep it a modular monolith until there is an actual reason to split it.**