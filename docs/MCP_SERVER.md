# StoreOS Model Context Protocol (MCP) Server

The **StoreOS Model Context Protocol (MCP) Server** allows AI assistants (Claude Desktop, Google Antigravity / Gemini CLI, Cursor, Windsurf) to securely inspect and control all operations across the StoreOS retail, POS, inventory, repairs, and financial management platform.

---

## 1. Architecture & Design Principles

```
  +-----------------------------------------------------------+
  |                   AI Assistant / MCP Client               |
  |             (Claude Desktop / Antigravity / Cursor)       |
  +-----------------------------------------------------------+
                               |
                   JSON-RPC 2.0 via stdio
                               |
  +-----------------------------------------------------------+
  |              StoreOS MCP Server (tools/storeos_mcp.py)    |
  |  - Zero external dependencies (pure Python 3 stdlib)      |
  |  - UTF-8 stream hygiene (100% logging to stderr)          |
  |  - Auto multi-tenant resolution (Host: apex.localhost)    |
  |  - Auto Idempotency-Key UUID injection on POST/PUT        |
  |  - 50+ Curated Tools + Universal API Gateway Tool         |
  +-----------------------------------------------------------+
                               |
                      HTTP REST / JSON
                               |
  +-----------------------------------------------------------+
  |                 StoreOS API (localhost:5000)              |
  |  - ASP.NET Core 10 Web API                                |
  |  - PostgreSQL 16 with Row-Level Security (RLS)            |
  |  - Hangfire, MinIO S3, QuestPDF, Serilog                  |
  +-----------------------------------------------------------+
```

### Key Architectural Tenets:
1. **Zero External Dependencies**: Pure Python 3 standard library (`urllib.request`, `json`, `sys`, `os`, `uuid`). Works identically across Windows, macOS, Linux, and Android Termux (Debian).
2. **Stdio Stream Hygiene**: The JSON-RPC stream operates strictly over `sys.stdin` and `sys.stdout` with UTF-8 encoding. All internal telemetry and diagnostics route to `sys.stderr`, preventing stream corruption.
3. **Multi-Tenant Host Resolution**: Transparently routes requests with `Host: apex.localhost:5000` (configurable via `STOREOS_SUBDOMAIN`), allowing StoreOS `TenantResolver` to automatically map the tenant.
4. **Idempotency Guard**: All financial and mutative endpoints automatically receive a unique `Idempotency-Key` UUID header, ensuring safety and compliance with StoreOS idempotency pipelines.
5. **Universal Gateway**: In addition to curated tools, the `storeos_api_call` tool allows executing any of the 89 endpoints in the StoreOS OpenAPI specification.

---

## 2. Client Setup & Configuration

### A. Claude Desktop

Add the following to your Claude Desktop configuration file:
- **Windows**: `%APPDATA%\Claude\claude_desktop_config.json`
- **macOS**: `~/Library/Application Support/Claude/claude_desktop_config.json`

```json
{
  "mcpServers": {
    "storeos": {
      "command": "python",
      "args": [
        "D:\\MyDocuments\\Programming\\dotnet\\StoreOS\\tools\\storeos_mcp.py"
      ],
      "env": {
        "STOREOS_API_URL": "http://localhost:5000",
        "STOREOS_SUBDOMAIN": "apex",
        "STOREOS_EMAIL": "owner@apex.example",
        "STOREOS_PASSWORD": "Demo@12345"
      }
    }
  }
}
```

> **Tip**: You can print your exact platform configuration path by running:
> ```bash
> python tools/storeos_mcp.py --config claude
> ```

---

### B. Google Antigravity / Gemini CLI

Add to your project or global `.gemini/settings.json` (or MCP configuration):

```json
{
  "mcpServers": {
    "storeos": {
      "command": "python",
      "args": [
        "D:\\MyDocuments\\Programming\\dotnet\\StoreOS\\tools\\storeos_mcp.py"
      ],
      "env": {
        "STOREOS_API_URL": "http://localhost:5000",
        "STOREOS_SUBDOMAIN": "apex"
      }
    }
  }
}
```

---

### C. Cursor

Add to `.cursor/mcp.json`:

```json
{
  "storeos": {
    "command": "python",
    "args": [
      "D:\\MyDocuments\\Programming\\dotnet\\StoreOS\\tools\\storeos_mcp.py"
    ],
    "env": {
      "STOREOS_API_URL": "http://localhost:5000",
      "STOREOS_SUBDOMAIN": "apex"
    }
  }
}
```

---

## 3. Environment Variables Reference

| Variable | Default Value | Description |
| :--- | :--- | :--- |
| `STOREOS_API_URL` | `http://localhost:5000` | Base URL of the running StoreOS ASP.NET Core API. |
| `STOREOS_SUBDOMAIN` | `apex` | Multi-tenant subdomain used for Host header routing. |
| `STOREOS_EMAIL` | `owner@apex.example` | Account email for automatic login authentication. |
| `STOREOS_PASSWORD` | `Demo@12345` | Account password for automatic login authentication. |
| `STOREOS_AUTH_TOKEN` | *(empty)* | Optional raw Bearer token override (bypasses login). |
| `STOREOS_BRANCH_ID` | *(default branch)* | Default branch UUID for operations and `X-Branch-Id` header. |
| `STOREOS_TENANT_ID` | *(auto-resolved)* | Explicit tenant UUID override if needed. |

---

## 4. Curated Tools Reference

### System & Context
- `storeos_status`: Check API health, active user, resolved tenant, and branch.
- `storeos_set_branch(branch_id)`: Switch the active branch ID for subsequent mutations.
- `storeos_branches_list()`: List all physical branches for the tenant.

### Catalog & Products
- `storeos_products_list(search, category, page, page_size)`: Search/filter product catalog.
- `storeos_products_get(product_id)`: Retrieve detailed product record.
- `storeos_products_create(name, sku, barcode, category, selling_price, purchase_price, tax_rate_percent, min_stock_alert, is_serialized)`: Add new product.
- `storeos_products_update(product_id, name, selling_price, purchase_price, min_stock_alert)`: Update product.

### Inventory & Transfers
- `storeos_inventory_list(branch_id, search, page, page_size)`: Stock balances across branches.
- `storeos_inventory_low_stock(branch_id)`: Alert items below reorder thresholds.
- `storeos_inventory_valuation(branch_id)`: Calculate inventory asset valuation.
- `storeos_inventory_adjust(branch_id, product_id, quantity_delta, reason, notes)`: Manual stock correction.
- `storeos_inventory_transfer_out(from_branch_id, to_branch_id, items, notes)`: Initiate inter-branch stock transfer.
- `storeos_inventory_transfers_pending()`: List pending transfers.
- `storeos_inventory_transfer_receive(transfer_id, branch_id)`: Receive transferred items.

### Point of Sale (POS) & Checkout
- `storeos_pos_search(query)`: Fast search by barcode, IMEI, or product name.
- `storeos_pos_get_product(product_id)`: Real-time price tiers and stock level.
- `storeos_pos_checkout(branch_id, customer_id, items, payments, notes)`: Complete sale with tender splits.
- `storeos_pos_create_estimate(branch_id, customer_id, items, notes)`: Create price quotation.
- `storeos_pos_convert_estimate(estimate_id, payments)`: Convert estimate directly into sale invoice.

### Sales & Orders
- `storeos_sales_list(branch_id, customer_id, from_date, to_date, page, page_size)`: List invoices.
- `storeos_sales_get(sale_id)`: Full invoice details and payments.
- `storeos_sales_record_payment(sale_id, amount, method, notes)`: Settle unpaid balance.
- `storeos_sales_refund(sale_id, amount, reason, refund_method)`: Process return / refund.
- `storeos_sales_void(sale_id, reason)`: Void erroneous sale invoice.

### Customers
- `storeos_customers_list(query, page, page_size)`: Search customers by name or phone.
- `storeos_customers_get(customer_id)`: Profile and credit balance.
- `storeos_customers_create(name, phone, email, address, credit_limit)`: Register customer.
- `storeos_customers_update(customer_id, name, phone, email, address, credit_limit)`: Update profile.
- `storeos_customers_ledger(customer_id)`: View ledger transactions and credit balance.
- `storeos_customers_sales(customer_id)`: Customer purchase history.

### Repairs & Device Services
- `storeos_repairs_list(branch_id, status, search, page, page_size)`: List tickets by status.
- `storeos_repairs_get(job_id)`: Complete repair record, faults, diagnosis, parts.
- `storeos_repairs_create(branch_id, customer_id, device_brand, device_model, serial_or_imei, issue_description, passcode, estimated_cost)`: Open intake ticket.
- `storeos_repairs_update_diagnosis(job_id, diagnosis_notes, estimated_completion_date, repair_category)`: Update technician notes.
- `storeos_repairs_record_approval(job_id, approved, notes)`: Customer quote approval.
- `storeos_repairs_change_status(job_id, status, notes)`: Advance repair workflow.
- `storeos_repairs_bill(job_id, payments, discount)`: Settle and invoice repair.
- `storeos_repairs_deliver(job_id, notes)`: Handover device to customer.

### Suppliers & Purchases
- `storeos_suppliers_list(search)`: List vendors and payables.
- `storeos_suppliers_create(name, contact_person, phone, email, gstin, address)`: Add vendor profile.
- `storeos_purchases_stock_in(branch_id, supplier_id, supplier_invoice_number, invoice_date, items)`: Inward purchase stock.
- `storeos_suppliers_record_payment(supplier_id, amount, payment_method, notes)`: Settle supplier payable.
- `storeos_suppliers_ledger(supplier_id)`: Vendor purchase and payment ledger.

### Expenses & Liabilities
- `storeos_expenses_list(branch_id, category, from_date, to_date)`: Operational shop expenses.
- `storeos_expenses_create(branch_id, category, amount, description, payment_method)`: Record expense.
- `storeos_expenses_monthly_total(year, month, branch_id)`: Monthly expense totals.
- `storeos_liabilities_list()`: Active liabilities and loans.
- `storeos_liabilities_repayment(liability_id, amount, payment_method, notes)`: Record repayment.

### Reports & Analytics
- `storeos_reports_dashboard(branch_id)`: Executive dashboard summary (revenue, profit, sales, repairs).
- `storeos_reports_daily_sales(date, branch_id)`: Detailed daily sales audit.
- `storeos_reports_compare_branches(from_date, to_date)`: Comparative multi-branch analytics.
- `storeos_exports_gstr1(year, month)`: Tax GSTR-1 compliance export data.

### Administration
- `storeos_users_list()`: List staff accounts and roles.
- `storeos_settings_get(key)`: Read store configuration.
- `storeos_settings_update(key, value)`: Update store configuration.

### Universal API Gateway Tool
- `storeos_api_call(method, path, query, body, branch_id, idempotency_key)`:
  Execute **any** StoreOS API endpoint with custom parameters. Ensures 100% route coverage across all current and future endpoints.

---

## 5. Resources & Prompts

### Resources
- `storeos://system/status`: Real-time health, active user, resolved tenant, branch.
- `storeos://reports/dashboard`: Live executive KPI snapshot.
- `storeos://system/openapi`: Full OpenAPI 3.0 specification covering all 89 endpoints.

### Prompts
- `daily-briefing`: Morning or end-of-day store briefing summarizing sales, repairs, and low stock.
- `repair-intake`: Guided intake checklist for customer devices.
- `pos-checkout-assistant`: Cart assembly and payment tender calculation helper.
- `stock-audit`: Inventory shortage audit and reorder purchase advice.

---

## 6. Testing & Self-Verification

Run the automated test suite directly from your terminal:

```bash
# In-process MCP protocol and schema compliance test:
python tools/storeos_mcp.py --test

# Optional live test against running backend API:
python tools/storeos_mcp.py --test --live
```
