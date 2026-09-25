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
                        ┌────────────────────────────┐
   Browser (Angular     │  CDN / Static host         │
   PWA, desktop &       │  Angular bundle (nginx)    │
   tablet)              └─────────────┬──────────────┘
                                       │ HTTPS, JWT, X-Branch-Id
                        ┌─────────────▼──────────────┐
                        │  ASP.NET Core modular      │
                        │  monolith (API)            │
                        │  ┌──────────────────────┐  │
                        │  │ Modules: Identity,   │  │
                        │  │ Tenancy & Branches,  │  │
                        │  │ Catalog, Inventory,  │  │
                        │  │ Sales, Customers,    │  │
                        │  │ Suppliers, Repairs,  │  │
                        │  │ Expenses, Liabilities│  │
                        │  │ Reporting, Platform  │  │
                        │  └──────────────────────┘  │
                        │  CQRS · EF Core · Hangfire │
                        └───┬──────────┬───────────┬─┘
                            │          │           │
                   ┌────────▼───┐ ┌────▼─────┐ ┌───▼────────────┐
                   │ PostgreSQL │ │ S3-like  │ │ OTLP collector │
                   │ (managed,  │ │ object   │ │ → Grafana /    │
                   │ RLS on)    │ │ storage  │ │ App Insights   │
                   └────────────┘ └──────────┘ └────────────────┘
```

**Runtime topology per environment:** static site + API container(s) + managed PostgreSQL + object storage + secret manager. Scale by adding API replicas; PostgreSQL stays single-writer — correct for POS semantics.

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
    var margin = sell - cost;
    if (margin <= 0) return LineTax.Zero;
    var taxable = Money.Round2(margin / (1 + gstRate / 100m));
    var totalGst = Money.Round2(margin - taxable);
    var cgst = Money.Round2(totalGst / 2m);
    return new LineTax(taxable, cgst, Money.Round2(totalGst - cgst));
}
```

**Margin-scheme cost source rule:** Margin scheme is a per-sale-line toggle, but `products.purchase_price` is merely a tenant-wide catalog default. For used phones, acquisition cost varies unit by unit:
- **When an IMEI is present on the line:** `cost = inventory_imeis.purchase_price` (the actual acquisition cost recorded when that specific physical unit was purchased/stocked-in).
- **When no IMEI is present:** `cost = branch_inventory.cost_price` (falling back to `products.purchase_price` if unseeded).
This ensures each used phone accurately reflects its individual gross margin and GST liability, rather than reporting an identical, incorrect margin across units of the same model.

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
  expires_at timestamptz NOT NULL,
  revoked_at timestamptz NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

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
  total_purchases numeric(18,2) NOT NULL DEFAULT 0,    -- projection
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_customers_tenant_phone ON customers (tenant_id, phone);

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

```sql
ALTER TABLE sales ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_branch_isolation ON sales USING (
  tenant_id = current_setting('app.tenant_id')::uuid
  AND (
    current_setting('app.all_branches', true) = 'true'
    OR branch_id = ANY (string_to_array(current_setting('app.branch_ids', true), ',')::uuid[])
  )
);
```

Apply the same policy shape to every branch-scoped table.

#### Connection Pooling vs. RLS Session State (Production Safety Mandate)

`current_setting('app.tenant_id')` set via session-level `SET` survives for the life of a PostgreSQL connection session, NOT an individual HTTP request. Under connection pooling:
- **Catastrophic Failure Mode (Transaction-Mode Pooling):** If an external connection pooler like PgBouncer is deployed in **transaction pooling mode** (`pool_mode = transaction`), physical connections are recycled across arbitrary clients between transactions without resetting GUC session variables (`app.tenant_id`). Tenant A's connection will serve Tenant B's next query, causing silent cross-tenant data leakage under concurrent load.
- **Architectural Decision & Configuration:**
  1. **Primary Strategy (Npgsql Native Pooling):** ASP.NET Core relies on Npgsql's built-in connection pool directly against PostgreSQL. External poolers are omitted in standard deployments.
  2. **Transaction-Scoped Context via `SET LOCAL`:** For all transactional operations, EF Core interceptors (`DbTransactionInterceptor` / `SaveChangesInterceptor`) issue `SET LOCAL` commands:
     ```sql
     SET LOCAL app.tenant_id = @tenantId;
     SET LOCAL app.branch_ids = @branchIds;
     SET LOCAL app.all_branches = @allBranches;
     ```
     Because `SET LOCAL` is strictly scoped to the active transaction, settings automatically evaporate on `COMMIT` or `ROLLBACK`, guaranteeing zero context leakage across pooled physical connections.
  3. **External Pooler Constraint (PgBouncer):** If PgBouncer is required in high-scale infrastructure, it **MUST be configured exclusively in session pooling mode (`pool_mode = session`)**, NEVER `transaction` or `statement` mode.
  4. **Primary Line of Defense:** EF Core query filters (§9.1) execute inside application memory and enforce tenant/branch scoping on every generated SQL query as the first line of defense; PostgreSQL RLS functions as defense-in-depth.

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
                      else branch_inventory.cost_price / products.purchase_price.
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

`POST /pos/estimate/{id}/convert` enables cashiers to convert an accepted estimate directly into a binding tax invoice without re-entering the cart:

```text
BEGIN
  1. Load estimate by ID (verifying invoice_type = 'Estimate' and tenant/branch access).
  2. Verify current stock availability for all physical items and ensure specified IMEIs
     are currently 'InStock' at the branch.
  3. Validate customer credit limit if converting to an unpaid/credit sale.
  4. Reserve new Tax Invoice number from invoice_sequences.
  5. Invoke IInventoryStockService: decrement branch stock and transition IMEIs to 'Sold'.
  6. INSERT sales (invoice_type = 'TaxInvoice', invoice_number = new sequence,
     converted_from_estimate_id = estimate.id) and sale_items.
  7. INSERT inventory_movements (Sale) for all physical lines.
  8. Capture initial payments and record customer_ledger_entries if balance remains.
  9. Update original estimate record (status = 'Converted', linked_tax_invoice_id = sale.id).
 10. INSERT audit_log (with correlation-id).
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
| Auth | `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout`, `GET /auth/me` (incl. branches, roles, permissions) |
| Branches | `GET/POST/PATCH /branches`, `GET /branches/{id}`, user assignment endpoints (`user_branches`, `user_role_assignments`) |
| POS | `GET /pos/search?q=` (branch-scoped), `GET /pos/product/{id}` (incl. available IMEIs, branch sell price override), `POST /pos/checkout` (idempotent, validates stock/IMEI/credit limit), `POST /pos/estimate`, `POST /pos/estimate/{id}/convert` (converts estimate to tax invoice) |
| Sales | `GET /sales` (all filters/sort/page: status, payment mode, type, paid/due, financed, category, amount range, dates, branch), `GET /sales/{id}` (incl. IRN/QR data), `POST /sales/{id}/payments`, `POST /sales/{id}/void`, `POST /sales/{id}/refund`, `GET /sales/{id}/invoice.pdf` |
| Customers | `GET/POST/PATCH /customers` (incl. `credit_limit`, live cross-branch balance), `GET /customers/{id}/ledger` (per-invoice branch attribution), `GET /customers/{id}/sales` |
| Inventory | `GET/POST/PATCH /products`, `GET /inventory?branchIds=`, `GET /inventory/valuation?branchIds=`, `GET /inventory/low-stock`, `GET/POST /imeis`, `POST /inventory/adjustments` |
| Purchases | `POST /purchases` (stock-in with optional IMEIs), `GET /purchases`, `POST /imports/supplier-invoice` (deterministic mapping; ambiguous → manual mapping step; no AI) |
| Suppliers | `GET/POST/PATCH /suppliers`, `POST /suppliers/{id}/transactions`, `GET /suppliers/{id}/ledger?branchIds=`, `POST /supplier-returns`, `GET /supplier-returns/{id}/note.pdf` |
| Transfers | `POST /inventory/transfers` (out), `POST /inventory/transfers/{id}/receive` |
| Repairs | `GET/POST/PATCH /repair-services`, `GET/POST/PATCH /repair-jobs`, `POST /repair-jobs/{id}/status`, `POST /repair-jobs/{id}/parts`, `POST /repair-jobs/{id}/invoice` (deterministic two-step link), `GET /repair-jobs/{id}/ticket.pdf` |
| Expenses | `GET/POST/PATCH /expenses?from&to&branchIds=` |
| Liabilities | `GET/POST/PATCH /liabilities`, `POST /liabilities/{id}/repayments` |
| Reports | `GET /reports/dashboard?branchIds=`, `GET /reports/daily-sales?date&branchIds=`, `GET /reports/compare?date` (per-branch comparison), `GET /reports/inventory?branchIds=`, `GET /exports/gstr1?from&to` (grouped by GSTIN), `GET /exports/inventory?categories=&branchIds=` |
| Settings | `GET/PUT /settings` (tenant + branch scoped, series format: FY consecutive vs daily), finance providers CRUD |
| Platform | `GET /health/live`, `GET /health/ready`, `POST /tenant/export`, `POST /tenant/import` (record-level dedupe) |

- Query/report endpoints accept `branchIds` (multi) or `all`; branch users are restricted server-side to their visible set regardless of what they request.
- **OpenAPI → TypeScript client:** NSwag generates `frontend/src/app/core/api/` at build; CI regenerates and fails on drift. No hand-written API calls.
- **Rate limiting:** `POST /auth/login` 5/min/IP; global 200 req/min/user.

---

## 12. Frontend (Angular)

### 12.1 First-Run Shop Onboarding & Dynamic White-Labeling

The application operates as a **strict white-label solution**. The platform name `StoreOS` is strictly an internal architectural identifier and is completely invisible to cashiers, store managers, and retail customers.

1. **First-Run Onboarding Modal / Wizard:**
   - When an owner provisions a tenant or logs in for the first time, the onboarding flow immediately prompts:
     * **"What is your Shop / Business Name?"** (e.g., *"Apex Mobiles"*).
     * Optional brand details: logo upload, GSTIN, store address, phone numbers, email, and thermal receipt footer message.
   - Once submitted, the system saves `tenants.name` and initializes the branch settings.
2. **Dynamic Shop Identity Across the Entire Application:**
   - **Browser Tab `<title>`:** Driven dynamically by Angular's `Title` service: `{{ activeBranch.name }} | {{ currentTenant.name }}` (e.g., `Main Branch | Apex Mobiles`).
   - **Top Navigation Bar:** Displays the shop's custom brand name and logo front-and-center, accompanied by the active branch switcher.
   - **PWA & Desktop App Name:** Configured using the shop's name so cashiers install "Apex Mobiles POS" onto their tablets or desktops.
   - **Login & Lock Screen:** Dynamically renders the tenant's brand name and logo (resolved via subdomain or user account context), never vendor branding.
   - **Customer Invoices, Receipts, & PDFs:** QuestPDF templates format the header strictly using the shop's trade name, logo, address, and GSTIN.
   - **SMS / WhatsApp Notifications:** Messages originate exclusively under the shop's identity.

### 12.2 Structure

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
JWT + rotating refresh, revocable      Rate limiting on auth + global
Tenant isolation: query filters + RLS  Branch isolation: scoped filters + RLS
Server-recomputed prices/totals        Idempotency on money endpoints
Append-only audit + ledger tables      Permission-gated sensitive data (cost/profit)
Secrets in cloud secret manager        Dependency + image scanning in CI
Input validation on every endpoint     Cross-branch access tests (horizontal)
Correlation-ID on all requests/audits  Strict session-mode DB pooling
```

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

### 14.3 Backups & DR

- Managed PostgreSQL PITR (7–35 days) — platform level.
- Nightly `pg_dump` per environment → object storage, 30-day retention.
- **Monthly restore drill in CI/staging:** restore the latest dump into a scratch DB and run reconciliation assertions (row counts, ledger sums, `stock_qty` vs movements). Alert on failure.
- Per-tenant JSON export to object storage on schedule and on demand.

### 14.4 IaC

Terraform/Bicep: resource group, managed PostgreSQL, object storage + lifecycle rules, container host, secret manager, DNS/TLS, monitoring ingestion. Staging and production differ only by variables.

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
| No double-selling a physical unit (any branch) | IMEI unique index + status check |
| No sale above available stock (per branch) | Conditional update test |
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
3. **Record-level deduplication for incremental sync:**
   During the pilot phase (§17.4), the desktop app remains active as a fallback. An updated backup taken days or weeks later will have a different file hash. Instead of failing on file-hash collisions, `pz-migrate` dedupes per-record based on natural business keys:
   - Sales: `(tenant_id, original_invoice_number)`
   - IMEIs: `(tenant_id, imei)`
   - Customers: `(tenant_id, phone)`
   - Purchases: `(tenant_id, supplier_invoice_no, supplier_id, purchase_date)`
   Existing records are preserved without duplication; newly created desktop transactions are imported incrementally.
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
| Cross-branch data leakage | Two independent layers (query filters + RLS) + horizontal access tests in CI |
| RLS + connection pool tenant data leak | Npgsql internal pooling; `SET LOCAL` within all transactions; strict `pool_mode = session` if external poolers (PgBouncer) deployed |
| GST Rule 46(b) serial numbering challenge | Configurable sequence generator supporting `<BRANCH>/<FY>/<NNNNNN>` (consecutive across FY) vs daily; mandatory CA signoff before go-live |
| E-invoicing ₹5cr threshold per-PAN | Turnover aggregates across all branches under tenant's PAN; pre-wired schema (`irn`, `irn_ack_no`, `irn_qr_data`), PDF template ready, Phase 8 roadmap |
| Cross-module writes breaking architecture | Internal typed application services (`IInventoryStockService`, `ICustomerLedgerService`) orchestrated within ambient EF Core transaction |
| Margin scheme cost drift on used phones | Domain rule: acquisition cost pulled from specific `inventory_imeis.purchase_price`, not tenant catalog default |
| Unbounded cross-branch customer debt | Consolidated live balance displayed at POS checkout; configurable customer credit limit with warning/override gate |
| Cloud cost sensitivity for small shops | Tenant density on shared DB; small container + basic PostgreSQL per environment |
| Incremental migration failures during pilot | Record-level deduplication on business keys (invoice no, IMEI, phone) instead of whole-file hash |
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
- [ ] RLS + query filters verified against cross-tenant **and** cross-branch access
- [ ] Idempotency proven on checkout/payment replay (scoped by endpoint in PK)
- [ ] Request Correlation-ID (`X-Correlation-ID`) verified across HTTP headers, Serilog logs, OpenTelemetry traces, and `audit_log`
- [ ] Branch-scoped invoice numbering proven collision-free under parallel load (with CA-confirmed FY sequence format)
- [ ] Estimate-to-Invoice conversion (`POST /pos/estimate/{id}/convert`) verified end-to-end
- [ ] Customer credit limit and live cross-branch balance display verified at POS checkout
- [ ] Two-step repair billing insert sequence verified free of circular FK deadlocks
- [ ] Extended nightly reconciliation worker validates stock, payments, customer revenue, credit balances, and supplier payables
- [ ] Incremental desktop backup migration verified with record-level deduplication
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