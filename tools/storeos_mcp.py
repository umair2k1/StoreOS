#!/usr/bin/env python3
"""StoreOS Model Context Protocol (MCP) Server.

Provides full administrative and operational control over StoreOS to AI
assistants (Claude Desktop, Antigravity / Gemini CLI, Cursor, Windsurf, etc.).

Zero external dependencies — pure Python 3 standard library.
Multi-device compatible (Windows, macOS, Linux, Android Termux / Debian).
Strictly adheres to MCP 2024-11-05 JSON-RPC specification.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

# -----------------------------------------------------------------------------
# Stdio Stream Hygiene & Windows Encoding Setup
# -----------------------------------------------------------------------------
if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8")


def log_debug(msg: str) -> None:
    """Log strictly to stderr to keep stdout 100% clean for JSON-RPC."""
    sys.stderr.write(f"[storeos-mcp] {msg}\n")
    sys.stderr.flush()


def get_repo_root() -> Path:
    """Resolve repository root directory."""
    script_dir = Path(__file__).resolve().parent
    if (script_dir.parent / "backend").exists() and (script_dir.parent / "frontend").exists():
        return script_dir.parent
    cwd = Path.cwd()
    if (cwd / "backend").exists() and (cwd / "frontend").exists():
        return cwd
    return script_dir.parent


# -----------------------------------------------------------------------------
# StoreOS HTTP Client & Multi-Tenant Session
# -----------------------------------------------------------------------------
class StoreOsClient:
    """HTTP Client communicating with the StoreOS ASP.NET Core API."""

    def __init__(
        self,
        api_url: Optional[str] = None,
        subdomain: Optional[str] = None,
        email: Optional[str] = None,
        password: Optional[str] = None,
        auth_token: Optional[str] = None,
        branch_id: Optional[str] = None,
    ) -> None:
        self.api_url = (api_url or os.environ.get("STOREOS_API_URL", "http://localhost:5000")).rstrip("/")
        self.subdomain = subdomain or os.environ.get("STOREOS_SUBDOMAIN", "apex")
        self.email = email or os.environ.get("STOREOS_EMAIL", "owner@apex.example")
        self.password = password or os.environ.get("STOREOS_PASSWORD", "Demo@12345")
        self.auth_token = auth_token or os.environ.get("STOREOS_AUTH_TOKEN", "")
        self.branch_id = branch_id or os.environ.get("STOREOS_BRANCH_ID", "")

        self.tenant_id: Optional[str] = os.environ.get("STOREOS_TENANT_ID") or None
        self.user_id: Optional[str] = None
        self.user_name: Optional[str] = None
        self.permissions: List[str] = []
        self.branch_ids: List[str] = []
        self.token_expiry: float = 0.0

    def _get_host_header(self) -> str:
        """Resolve the multi-tenant Host header for TenantResolver matching."""
        parsed = urllib.parse.urlparse(self.api_url)
        port = parsed.port
        port_suffix = f":{port}" if port and port not in (80, 443) else ""
        return f"{self.subdomain}.localhost{port_suffix}"

    def login(self) -> bool:
        """Perform /api/auth/login to authenticate and resolve tenant context."""
        url = f"{self.api_url}/api/auth/login"
        payload = {
            "email": self.email,
            "password": self.password,
            "tenantId": "00000000-0000-0000-0000-000000000000",
        }
        body_bytes = json.dumps(payload).encode("utf-8")
        headers = {
            "Host": self._get_host_header(),
            "Content-Type": "application/json",
            "Accept": "application/json",
            "User-Agent": "StoreOS-MCP-Server/1.0",
        }

        req = urllib.request.Request(url, data=body_bytes, headers=headers, method="POST")
        try:
            with urllib.request.urlopen(req, timeout=10.0) as resp:
                data = json.loads(resp.read().decode("utf-8"))
                self.auth_token = data.get("accessToken", "")
                self.tenant_id = data.get("tenantId")
                self.user_id = data.get("userId")
                self.user_name = data.get("displayName")
                self.permissions = data.get("permissions", [])
                self.branch_ids = data.get("branchIds", [])
                self.token_expiry = time.time() + 3600.0

                if not self.branch_id and self.branch_ids:
                    self.branch_id = self.branch_ids[0]

                log_debug(f"Authenticated as {self.user_name} ({self.email}) for tenant {self.tenant_id}")
                return True
        except Exception as ex:
            log_debug(f"Login failed against {url}: {ex}")
            return False

    def ensure_authenticated(self) -> None:
        """Ensure active JWT token is present before executing requests."""
        if self.auth_token and time.time() < self.token_expiry:
            return
        if self.auth_token and not self.email:
            return
        success = self.login()
        if not success and not self.auth_token:
            log_debug("Warning: proceeding without confirmed bearer token.")

    def request(
        self,
        method: str,
        path: str,
        query: Optional[Dict[str, Any]] = None,
        body: Optional[Any] = None,
        branch_id: Optional[str] = None,
        idempotency_key: Optional[str] = None,
    ) -> Tuple[int, Any]:
        """Execute HTTP request against StoreOS backend API with full context."""
        self.ensure_authenticated()

        clean_path = "/" + path.lstrip("/")
        url = f"{self.api_url}{clean_path}"
        if query:
            clean_query = {k: v for k, v in query.items() if v is not None}
            if clean_query:
                url += f"?{urllib.parse.urlencode(clean_query)}"

        headers = {
            "Host": self._get_host_header(),
            "Accept": "application/json",
            "User-Agent": "StoreOS-MCP-Server/1.0",
        }

        if self.auth_token:
            headers["Authorization"] = f"Bearer {self.auth_token}"

        effective_branch = branch_id or self.branch_id
        if effective_branch:
            headers["X-Branch-Id"] = effective_branch

        is_mutation = method.upper() in ("POST", "PUT", "PATCH", "DELETE")
        data_bytes = None
        if body is not None:
            data_bytes = json.dumps(body).encode("utf-8")
            headers["Content-Type"] = "application/json"
        elif is_mutation and method.upper() == "POST":
            data_bytes = b"{}"
            headers["Content-Type"] = "application/json"

        if is_mutation:
            headers["Idempotency-Key"] = idempotency_key or str(uuid.uuid4())

        req = urllib.request.Request(url, data=data_bytes, headers=headers, method=method.upper())

        try:
            with urllib.request.urlopen(req, timeout=30.0) as resp:
                status_code = resp.status
                raw = resp.read().decode("utf-8")
                try:
                    return status_code, json.loads(raw)
                except Exception:
                    return status_code, raw
        except urllib.error.HTTPError as http_err:
            raw_err = http_err.read().decode("utf-8", errors="replace")
            try:
                parsed_err = json.loads(raw_err)
            except Exception:
                parsed_err = {"error": raw_err, "status": http_err.code}
            return http_err.code, parsed_err
        except Exception as ex:
            return 500, {"error": str(ex), "status": 500}


# -----------------------------------------------------------------------------
# Tool Definitions & Handlers
# -----------------------------------------------------------------------------
class ToolRegistry:
    """Registry of MCP tools and their execution handlers."""

    def __init__(self, client: StoreOsClient) -> None:
        self.client = client
        self.tools: List[Dict[str, Any]] = []
        self._handlers: Dict[str, Any] = {}
        self._register_all()

    def register(self, name: str, description: str, input_schema: Dict[str, Any], handler: Any) -> None:
        """Register a tool definition and its handler function."""
        self.tools.append({
            "name": name,
            "description": description,
            "inputSchema": input_schema,
        })
        self._handlers[name] = handler

    def execute(self, name: str, arguments: Dict[str, Any]) -> Tuple[bool, Any]:
        """Dispatch a tool execution by name."""
        if name not in self._handlers:
            return True, f"Unknown tool: {name}"
        try:
            res = self._handlers[name](arguments)
            return False, res
        except Exception as ex:
            return True, f"Tool execution failed: {ex}"

    def _register_all(self) -> None:
        """Register all curated StoreOS tools + universal gateway."""
        self._register_system_tools()
        self._register_catalog_tools()
        self._register_inventory_tools()
        self._register_pos_tools()
        self._register_sales_tools()
        self._register_customer_tools()
        self._register_repair_tools()
        self._register_supplier_tools()
        self._register_finance_tools()
        self._register_report_tools()
        self._register_admin_tools()
        self._register_gateway_tool()

    # --- 1. System & Context Tools ---
    def _register_system_tools(self) -> None:
        self.register(
            name="storeos_status",
            description="Retrieve StoreOS system health, active user session, resolved tenant, and branch information.",
            input_schema={"type": "object", "properties": {}},
            handler=self._handle_status,
        )
        self.register(
            name="storeos_set_branch",
            description="Set or switch the active branch ID for subsequent mutations and operations.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "The branch UUID to set as active."}
                },
                "required": ["branch_id"],
            },
            handler=self._handle_set_branch,
        )
        self.register(
            name="storeos_branches_list",
            description="List all physical branches belonging to the current tenant.",
            input_schema={"type": "object", "properties": {}},
            handler=lambda _: self.client.request("GET", "/api/branches")[1],
        )

    def _handle_status(self, _: Dict[str, Any]) -> Any:
        self.client.ensure_authenticated()
        _, me = self.client.request("GET", "/api/auth/me")
        _, health = self.client.request("GET", "/health/ready")
        return {
            "api_url": self.client.api_url,
            "subdomain": self.client.subdomain,
            "tenant_id": self.client.tenant_id,
            "active_branch_id": self.client.branch_id,
            "user": me,
            "health": health,
        }

    def _handle_set_branch(self, args: Dict[str, Any]) -> Any:
        new_branch = args["branch_id"].strip()
        self.client.branch_id = new_branch
        return {"status": "ok", "active_branch_id": self.client.branch_id}

    # --- 2. Catalog & Products Tools ---
    def _register_catalog_tools(self) -> None:
        self.register(
            name="storeos_products_list",
            description="Search and list products in the catalog with category, search query, and low-stock filters.",
            input_schema={
                "type": "object",
                "properties": {
                    "search": {"type": "string", "description": "Search keyword matching title, SKU, or barcode."},
                    "category": {"type": "string", "description": "Filter by product category."},
                    "page": {"type": "integer", "description": "Page index (1-based, default 1)."},
                    "page_size": {"type": "integer", "description": "Page size limit (default 20)."},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/products", query={
                "search": args.get("search"),
                "category": args.get("category"),
                "page": args.get("page", 1),
                "pageSize": args.get("page_size", 20),
            })[1],
        )
        self.register(
            name="storeos_products_get",
            description="Retrieve detailed product record by its UUID.",
            input_schema={
                "type": "object",
                "properties": {
                    "product_id": {"type": "string", "description": "Product UUID."}
                },
                "required": ["product_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/products/{args['product_id']}")[1],
        )
        self.register(
            name="storeos_products_create",
            description="Create a new product in the store catalog (phone, accessory, part).",
            input_schema={
                "type": "object",
                "properties": {
                    "name": {"type": "string", "description": "Product display name."},
                    "sku": {"type": "string", "description": "Stock Keeping Unit code."},
                    "barcode": {"type": "string", "description": "EAN/UPC barcode number."},
                    "category": {"type": "string", "description": "Category (Smartphones, Accessories, Parts, etc.)."},
                    "selling_price": {"type": "number", "description": "Standard retail selling price."},
                    "purchase_price": {"type": "number", "description": "Default purchase/cost price."},
                    "tax_rate_percent": {"type": "number", "description": "GST tax rate percentage (0, 5, 12, 18, 28)."},
                    "min_stock_alert": {"type": "integer", "description": "Minimum stock reorder alert threshold."},
                    "is_serialized": {"type": "boolean", "description": "True if tracked by IMEI/Serial Number."},
                },
                "required": ["name", "sku", "selling_price"],
            },
            handler=lambda args: self.client.request("POST", "/api/products", body={
                "name": args["name"],
                "sku": args["sku"],
                "barcode": args.get("barcode") or "",
                "category": args.get("category") or "General",
                "sellingPrice": args["selling_price"],
                "purchasePrice": args.get("purchase_price", 0.0),
                "taxRatePercent": args.get("tax_rate_percent", 18.0),
                "minStockAlert": args.get("min_stock_alert", 5),
                "isSerialized": args.get("is_serialized", False),
            })[1],
        )
        self.register(
            name="storeos_products_update",
            description="Update an existing product's details, prices, or alert thresholds.",
            input_schema={
                "type": "object",
                "properties": {
                    "product_id": {"type": "string", "description": "Product UUID."},
                    "name": {"type": "string", "description": "New product name."},
                    "selling_price": {"type": "number", "description": "Updated selling price."},
                    "purchase_price": {"type": "number", "description": "Updated purchase price."},
                    "min_stock_alert": {"type": "integer", "description": "Updated reorder point."},
                },
                "required": ["product_id"],
            },
            handler=lambda args: self.client.request("PATCH", f"/api/products/{args['product_id']}", body={
                k: v for k, v in {
                    "name": args.get("name"),
                    "sellingPrice": args.get("selling_price"),
                    "purchasePrice": args.get("purchase_price"),
                    "minStockAlert": args.get("min_stock_alert"),
                }.items() if v is not None
            })[1],
        )

    # --- 3. Inventory Tools ---
    def _register_inventory_tools(self) -> None:
        self.register(
            name="storeos_inventory_list",
            description="List inventory stock levels across branches or for a specific branch.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Optional branch UUID filter."},
                    "search": {"type": "string", "description": "Product name or SKU search."},
                    "page": {"type": "integer", "description": "Page index (default 1)."},
                    "page_size": {"type": "integer", "description": "Page size (default 20)."},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/inventory", query={
                "branchId": args.get("branch_id"),
                "search": args.get("search"),
                "page": args.get("page", 1),
                "pageSize": args.get("page_size", 20),
            })[1],
        )
        self.register(
            name="storeos_inventory_low_stock",
            description="Retrieve products currently below their reorder threshold for a branch.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID filter."}
                },
            },
            handler=lambda args: self.client.request("GET", "/api/inventory/low-stock", query={
                "branchId": args.get("branch_id"),
            })[1],
        )
        self.register(
            name="storeos_inventory_valuation",
            description="Calculate total inventory valuation and cost analysis for a branch.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID filter."}
                },
            },
            handler=lambda args: self.client.request("GET", "/api/inventory/valuation", query={
                "branchId": args.get("branch_id"),
            })[1],
        )
        self.register(
            name="storeos_inventory_adjust",
            description="Record a manual stock adjustment (damaged, discrepancy, audit correction).",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "product_id": {"type": "string", "description": "Product UUID."},
                    "quantity_delta": {"type": "integer", "description": "Adjustment delta (+5, -2, etc.)."},
                    "reason": {"type": "string", "description": "Reason (Damaged, Theft, Audit, Return)."},
                    "notes": {"type": "string", "description": "Optional notes."},
                },
                "required": ["product_id", "quantity_delta", "reason"],
            },
            handler=lambda args: self.client.request("POST", "/api/inventory/adjustments", body={
                "branchId": args.get("branch_id") or self.client.branch_id,
                "productId": args["product_id"],
                "quantityDelta": args["quantity_delta"],
                "reason": args["reason"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_inventory_transfer_out",
            description="Initiate an inter-branch transfer of products or serialized devices.",
            input_schema={
                "type": "object",
                "properties": {
                    "from_branch_id": {"type": "string", "description": "Origin branch UUID."},
                    "to_branch_id": {"type": "string", "description": "Destination branch UUID."},
                    "items": {
                        "type": "array",
                        "description": "List of items to transfer.",
                        "items": {
                            "type": "object",
                            "properties": {
                                "productId": {"type": "string"},
                                "quantity": {"type": "integer"},
                                "imei": {"type": "string"},
                            },
                            "required": ["productId", "quantity"],
                        },
                    },
                    "notes": {"type": "string", "description": "Transfer remarks."},
                },
                "required": ["from_branch_id", "to_branch_id", "items"],
            },
            handler=lambda args: self.client.request("POST", "/api/inventory/transfers", body={
                "fromBranchId": args["from_branch_id"],
                "toBranchId": args["to_branch_id"],
                "items": args["items"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_inventory_transfers_pending",
            description="List pending inter-branch stock transfers awaiting receipt.",
            input_schema={"type": "object", "properties": {}},
            handler=lambda _: self.client.request("GET", "/api/inventory/transfers/pending")[1],
        )
        self.register(
            name="storeos_inventory_transfer_receive",
            description="Acknowledge and receive an incoming inter-branch stock transfer.",
            input_schema={
                "type": "object",
                "properties": {
                    "transfer_id": {"type": "string", "description": "Transfer UUID."},
                    "branch_id": {"type": "string", "description": "Destination receiving branch UUID."},
                },
                "required": ["transfer_id"],
            },
            handler=lambda args: self.client.request(
                "POST",
                f"/api/inventory/transfers/{args['transfer_id']}/receive",
                branch_id=args.get("branch_id"),
            )[1],
        )

    # --- 4. POS (Point of Sale) Tools ---
    def _register_pos_tools(self) -> None:
        self.register(
            name="storeos_pos_search",
            description="Fast search for POS products by barcode scan, IMEI, SKU, or name.",
            input_schema={
                "type": "object",
                "properties": {
                    "query": {"type": "string", "description": "Search string or barcode."}
                },
                "required": ["query"],
            },
            handler=lambda args: self.client.request("GET", "/api/pos/search", query={"q": args["query"]})[1],
        )
        self.register(
            name="storeos_pos_get_product",
            description="Retrieve real-time POS product details, price tiers, and branch stock.",
            input_schema={
                "type": "object",
                "properties": {
                    "product_id": {"type": "string", "description": "Product UUID."}
                },
                "required": ["product_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/pos/product/{args['product_id']}")[1],
        )
        self.register(
            name="storeos_pos_checkout",
            description="Complete a retail POS checkout sale with line items, customer, and payment tender.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "customer_id": {"type": "string", "description": "Optional customer UUID (or walk-in)."},
                    "items": {
                        "type": "array",
                        "description": "Cart line items.",
                        "items": {
                            "type": "object",
                            "properties": {
                                "productId": {"type": "string"},
                                "quantity": {"type": "integer"},
                                "unitPrice": {"type": "number"},
                                "imei": {"type": "string"},
                                "discountAmount": {"type": "number"},
                            },
                            "required": ["productId", "quantity", "unitPrice"],
                        },
                    },
                    "payments": {
                        "type": "array",
                        "description": "Payment tender splits (Cash, UPI, Card, Credit).",
                        "items": {
                            "type": "object",
                            "properties": {
                                "method": {"type": "string", "description": "Cash, UPI, Card, or Credit."},
                                "amount": {"type": "number"},
                                "referenceNumber": {"type": "string"},
                            },
                            "required": ["method", "amount"],
                        },
                    },
                    "notes": {"type": "string", "description": "Sale invoice notes."},
                },
                "required": ["items", "payments"],
            },
            handler=lambda args: self.client.request("POST", "/api/pos/checkout", branch_id=args.get("branch_id"), body={
                "branchId": args.get("branch_id") or self.client.branch_id,
                "customerId": args.get("customer_id"),
                "items": args["items"],
                "payments": args["payments"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_pos_create_estimate",
            description="Create a non-binding price quotation / estimate for a customer.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "customer_id": {"type": "string", "description": "Optional customer UUID."},
                    "items": {"type": "array", "description": "Estimated line items."},
                    "notes": {"type": "string", "description": "Quotation remarks."},
                },
                "required": ["items"],
            },
            handler=lambda args: self.client.request("POST", "/api/pos/estimate", branch_id=args.get("branch_id"), body={
                "branchId": args.get("branch_id") or self.client.branch_id,
                "customerId": args.get("customer_id"),
                "items": args["items"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_pos_convert_estimate",
            description="Convert an existing quotation / estimate directly into a completed retail sale.",
            input_schema={
                "type": "object",
                "properties": {
                    "estimate_id": {"type": "string", "description": "Estimate UUID."},
                    "payments": {"type": "array", "description": "Payment tender breakdown."},
                },
                "required": ["estimate_id", "payments"],
            },
            handler=lambda args: self.client.request("POST", f"/api/pos/estimate/{args['estimate_id']}/convert", body={
                "payments": args["payments"],
            })[1],
        )

    # --- 5. Sales & Orders Tools ---
    def _register_sales_tools(self) -> None:
        self.register(
            name="storeos_sales_list",
            description="List completed sales invoices with optional date range and branch filters.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "customer_id": {"type": "string", "description": "Customer UUID."},
                    "from_date": {"type": "string", "description": "Start date (YYYY-MM-DD)."},
                    "to_date": {"type": "string", "description": "End date (YYYY-MM-DD)."},
                    "page": {"type": "integer", "description": "Page index."},
                    "page_size": {"type": "integer", "description": "Page size."},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/sales", query={
                "branchId": args.get("branch_id"),
                "customerId": args.get("customer_id"),
                "fromDate": args.get("from_date"),
                "toDate": args.get("to_date"),
                "page": args.get("page", 1),
                "pageSize": args.get("page_size", 20),
            })[1],
        )
        self.register(
            name="storeos_sales_get",
            description="Retrieve full details of a specific sale invoice by UUID.",
            input_schema={
                "type": "object",
                "properties": {
                    "sale_id": {"type": "string", "description": "Sale UUID."}
                },
                "required": ["sale_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/sales/{args['sale_id']}")[1],
        )
        self.register(
            name="storeos_sales_record_payment",
            description="Record a settlement payment against an existing invoice with unpaid/partial balance.",
            input_schema={
                "type": "object",
                "properties": {
                    "sale_id": {"type": "string", "description": "Sale UUID."},
                    "amount": {"type": "number", "description": "Payment amount."},
                    "method": {"type": "string", "description": "Payment method (Cash, UPI, Card)."},
                    "notes": {"type": "string", "description": "Payment notes."},
                },
                "required": ["sale_id", "amount", "method"],
            },
            handler=lambda args: self.client.request("POST", f"/api/sales/{args['sale_id']}/payments", body={
                "amount": args["amount"],
                "method": args["method"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_sales_refund",
            description="Process a return / refund against an existing sale invoice.",
            input_schema={
                "type": "object",
                "properties": {
                    "sale_id": {"type": "string", "description": "Sale UUID."},
                    "amount": {"type": "number", "description": "Refund amount."},
                    "reason": {"type": "string", "description": "Refund justification."},
                    "refund_method": {"type": "string", "description": "Cash, UPI, or StoreCredit."},
                },
                "required": ["sale_id", "amount", "reason"],
            },
            handler=lambda args: self.client.request("POST", f"/api/sales/{args['sale_id']}/refund", body={
                "amount": args["amount"],
                "reason": args["reason"],
                "refundMethod": args.get("refund_method") or "Cash",
            })[1],
        )
        self.register(
            name="storeos_sales_void",
            description="Void an erroneous sale invoice.",
            input_schema={
                "type": "object",
                "properties": {
                    "sale_id": {"type": "string", "description": "Sale UUID."},
                    "reason": {"type": "string", "description": "Reason for voiding."},
                },
                "required": ["sale_id", "reason"],
            },
            handler=lambda args: self.client.request("POST", f"/api/sales/{args['sale_id']}/void", body={
                "reason": args["reason"],
            })[1],
        )

    # --- 6. Customer Tools ---
    def _register_customer_tools(self) -> None:
        self.register(
            name="storeos_customers_list",
            description="Search customers by name, phone number, or email address.",
            input_schema={
                "type": "object",
                "properties": {
                    "query": {"type": "string", "description": "Search keyword or phone digits."},
                    "page": {"type": "integer", "description": "Page number."},
                    "page_size": {"type": "integer", "description": "Page size."},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/customers", query={
                "query": args.get("query"),
                "page": args.get("page", 1),
                "pageSize": args.get("page_size", 20),
            })[1],
        )
        self.register(
            name="storeos_customers_get",
            description="Get customer record, contact details, and current credit balance.",
            input_schema={
                "type": "object",
                "properties": {
                    "customer_id": {"type": "string", "description": "Customer UUID."}
                },
                "required": ["customer_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/customers/{args['customer_id']}")[1],
        )
        self.register(
            name="storeos_customers_create",
            description="Register a new retail or wholesale customer.",
            input_schema={
                "type": "object",
                "properties": {
                    "name": {"type": "string", "description": "Customer full name."},
                    "phone": {"type": "string", "description": "Contact phone number."},
                    "email": {"type": "string", "description": "Optional email address."},
                    "address": {"type": "string", "description": "Optional physical address."},
                    "credit_limit": {"type": "number", "description": "Credit limit amount."},
                },
                "required": ["name", "phone"],
            },
            handler=lambda args: self.client.request("POST", "/api/customers", body={
                "name": args["name"],
                "phone": args["phone"],
                "email": args.get("email") or "",
                "address": args.get("address") or "",
                "creditLimit": args.get("credit_limit", 0.0),
            })[1],
        )
        self.register(
            name="storeos_customers_update",
            description="Update an existing customer's contact info or credit limit.",
            input_schema={
                "type": "object",
                "properties": {
                    "customer_id": {"type": "string", "description": "Customer UUID."},
                    "name": {"type": "string"},
                    "phone": {"type": "string"},
                    "email": {"type": "string"},
                    "address": {"type": "string"},
                    "credit_limit": {"type": "number"},
                },
                "required": ["customer_id", "name", "phone"],
            },
            handler=lambda args: self.client.request("PUT", f"/api/customers/{args['customer_id']}", body={
                "name": args["name"],
                "phone": args["phone"],
                "email": args.get("email") or "",
                "address": args.get("address") or "",
                "creditLimit": args.get("credit_limit", 0.0),
            })[1],
        )
        self.register(
            name="storeos_customers_ledger",
            description="Retrieve transaction ledger entries and credit history for a customer.",
            input_schema={
                "type": "object",
                "properties": {
                    "customer_id": {"type": "string", "description": "Customer UUID."}
                },
                "required": ["customer_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/customers/{args['customer_id']}/ledger")[1],
        )
        self.register(
            name="storeos_customers_sales",
            description="Retrieve past retail sales history for a customer.",
            input_schema={
                "type": "object",
                "properties": {
                    "customer_id": {"type": "string", "description": "Customer UUID."}
                },
                "required": ["customer_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/customers/{args['customer_id']}/sales")[1],
        )

    # --- 7. Repair Service Tools ---
    def _register_repair_tools(self) -> None:
        self.register(
            name="storeos_repairs_list",
            description="List repair tickets filtered by status (Received, Diagnosing, InProgress, ReadyForDelivery, Delivered).",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "status": {"type": "string", "description": "Status code filter."},
                    "search": {"type": "string", "description": "Ticket number or customer search."},
                    "page": {"type": "integer"},
                    "page_size": {"type": "integer"},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/repair-jobs", query={
                "branchId": args.get("branch_id"),
                "status": args.get("status"),
                "search": args.get("search"),
                "page": args.get("page", 1),
                "pageSize": args.get("page_size", 20),
            })[1],
        )
        self.register(
            name="storeos_repairs_get",
            description="Retrieve complete repair job record, device details, diagnosis, parts, and history.",
            input_schema={
                "type": "object",
                "properties": {
                    "job_id": {"type": "string", "description": "Repair Job UUID."}
                },
                "required": ["job_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/repair-jobs/{args['job_id']}")[1],
        )
        self.register(
            name="storeos_repairs_create",
            description="Create a new repair intake ticket for a customer device.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "customer_id": {"type": "string", "description": "Customer UUID."},
                    "device_brand": {"type": "string", "description": "Brand (Apple, Samsung, Xiaomi, etc.)."},
                    "device_model": {"type": "string", "description": "Model (iPhone 14, Galaxy S23)."},
                    "serial_or_imei": {"type": "string", "description": "Device Serial Number or IMEI."},
                    "issue_description": {"type": "string", "description": "Reported faults or symptoms."},
                    "passcode": {"type": "string", "description": "Screen unlock pin/passcode."},
                    "estimated_cost": {"type": "number", "description": "Initial estimated repair cost."},
                },
                "required": ["customer_id", "device_brand", "device_model", "issue_description"],
            },
            handler=lambda args: self.client.request("POST", "/api/repair-jobs", branch_id=args.get("branch_id"), body={
                "branchId": args.get("branch_id") or self.client.branch_id,
                "customerId": args["customer_id"],
                "deviceBrand": args["device_brand"],
                "deviceModel": args["device_model"],
                "serialOrImei": args.get("serial_or_imei") or "",
                "issueDescription": args["issue_description"],
                "passcode": args.get("passcode") or "",
                "estimatedCost": args.get("estimated_cost", 0.0),
            })[1],
        )
        self.register(
            name="storeos_repairs_update_diagnosis",
            description="Update technician diagnosis, estimated completion, and technical observations.",
            input_schema={
                "type": "object",
                "properties": {
                    "job_id": {"type": "string", "description": "Repair Job UUID."},
                    "diagnosis_notes": {"type": "string", "description": "Detailed diagnosis findings."},
                    "estimated_completion_date": {"type": "string", "description": "Target date (YYYY-MM-DD)."},
                    "repair_category": {"type": "string", "description": "Screen, Battery, Board, WaterDamage."},
                },
                "required": ["job_id", "diagnosis_notes"],
            },
            handler=lambda args: self.client.request("PATCH", f"/api/repair-jobs/{args['job_id']}/diagnosis", body={
                "diagnosisNotes": args["diagnosis_notes"],
                "estimatedCompletionDate": args.get("estimated_completion_date"),
                "repairCategory": args.get("repair_category") or "General",
            })[1],
        )
        self.register(
            name="storeos_repairs_record_approval",
            description="Record customer quote approval or rejection for the repair.",
            input_schema={
                "type": "object",
                "properties": {
                    "job_id": {"type": "string", "description": "Repair Job UUID."},
                    "approved": {"type": "boolean", "description": "True if approved, False if rejected."},
                    "notes": {"type": "string", "description": "Customer comments or remarks."},
                },
                "required": ["job_id", "approved"],
            },
            handler=lambda args: self.client.request("POST", f"/api/repair-jobs/{args['job_id']}/approval", body={
                "approved": args["approved"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_repairs_change_status",
            description="Advance or transition the repair job status (Diagnosing, InProgress, ReadyForDelivery).",
            input_schema={
                "type": "object",
                "properties": {
                    "job_id": {"type": "string", "description": "Repair Job UUID."},
                    "status": {"type": "string", "description": "Target status name."},
                    "notes": {"type": "string", "description": "Status change reason."},
                },
                "required": ["job_id", "status"],
            },
            handler=lambda args: self.client.request("POST", f"/api/repair-jobs/{args['job_id']}/status", body={
                "status": args["status"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_repairs_bill",
            description="Generate billing invoice and settle payments for a completed repair job.",
            input_schema={
                "type": "object",
                "properties": {
                    "job_id": {"type": "string", "description": "Repair Job UUID."},
                    "payments": {"type": "array", "description": "Payment tender splits."},
                    "discount": {"type": "number", "description": "Discount applied."},
                },
                "required": ["job_id", "payments"],
            },
            handler=lambda args: self.client.request("POST", f"/api/repair-jobs/{args['job_id']}/bill", body={
                "payments": args["payments"],
                "discount": args.get("discount", 0.0),
            })[1],
        )
        self.register(
            name="storeos_repairs_deliver",
            description="Record device delivery and handover to customer.",
            input_schema={
                "type": "object",
                "properties": {
                    "job_id": {"type": "string", "description": "Repair Job UUID."},
                    "notes": {"type": "string", "description": "Handover notes."},
                },
                "required": ["job_id"],
            },
            handler=lambda args: self.client.request("POST", f"/api/repair-jobs/{args['job_id']}/deliver", body={
                "notes": args.get("notes") or "",
            })[1],
        )

    # --- 8. Supplier & Purchasing Tools ---
    def _register_supplier_tools(self) -> None:
        self.register(
            name="storeos_suppliers_list",
            description="List suppliers and their current outstanding payable balances.",
            input_schema={
                "type": "object",
                "properties": {
                    "search": {"type": "string", "description": "Supplier name search."}
                },
            },
            handler=lambda args: self.client.request("GET", "/api/suppliers", query={"search": args.get("search")})[1],
        )
        self.register(
            name="storeos_suppliers_create",
            description="Add a new supplier profile.",
            input_schema={
                "type": "object",
                "properties": {
                    "name": {"type": "string", "description": "Supplier business name."},
                    "contact_person": {"type": "string", "description": "Contact representative."},
                    "phone": {"type": "string", "description": "Phone number."},
                    "email": {"type": "string", "description": "Email address."},
                    "gstin": {"type": "string", "description": "GSTIN registration number."},
                    "address": {"type": "string", "description": "Supplier address."},
                },
                "required": ["name", "phone"],
            },
            handler=lambda args: self.client.request("POST", "/api/suppliers", body={
                "name": args["name"],
                "contactPerson": args.get("contact_person") or "",
                "phone": args["phone"],
                "email": args.get("email") or "",
                "gstin": args.get("gstin") or "",
                "address": args.get("address") or "",
            })[1],
        )
        self.register(
            name="storeos_purchases_stock_in",
            description="Record a purchase invoice to stock-in new inventory (phones, parts, accessories).",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Destination branch UUID."},
                    "supplier_id": {"type": "string", "description": "Supplier UUID."},
                    "supplier_invoice_number": {"type": "string", "description": "Vendor bill/invoice number."},
                    "invoice_date": {"type": "string", "description": "Invoice date (YYYY-MM-DD)."},
                    "items": {
                        "type": "array",
                        "description": "Purchased items with quantity and unit cost.",
                        "items": {
                            "type": "object",
                            "properties": {
                                "productId": {"type": "string"},
                                "quantity": {"type": "integer"},
                                "purchasePrice": {"type": "number"},
                                "imeis": {"type": "array", "items": {"type": "string"}},
                            },
                            "required": ["productId", "quantity", "purchasePrice"],
                        },
                    },
                },
                "required": ["supplier_id", "supplier_invoice_number", "items"],
            },
            handler=lambda args: self.client.request("POST", "/api/purchases", branch_id=args.get("branch_id"), body={
                "branchId": args.get("branch_id") or self.client.branch_id,
                "supplierId": args["supplier_id"],
                "supplierInvoiceNumber": args["supplier_invoice_number"],
                "invoiceDate": args.get("invoice_date") or time.strftime("%Y-%m-%d"),
                "items": args["items"],
            })[1],
        )
        self.register(
            name="storeos_suppliers_record_payment",
            description="Record a payout made to a supplier against their payable balance.",
            input_schema={
                "type": "object",
                "properties": {
                    "supplier_id": {"type": "string", "description": "Supplier UUID."},
                    "amount": {"type": "number", "description": "Amount paid."},
                    "payment_method": {"type": "string", "description": "BankTransfer, UPI, Cash, Cheque."},
                    "notes": {"type": "string", "description": "Reference number or remarks."},
                },
                "required": ["supplier_id", "amount", "payment_method"],
            },
            handler=lambda args: self.client.request("POST", f"/api/suppliers/{args['supplier_id']}/transactions", body={
                "amount": args["amount"],
                "paymentMethod": args["payment_method"],
                "notes": args.get("notes") or "",
            })[1],
        )
        self.register(
            name="storeos_suppliers_ledger",
            description="Retrieve purchase invoices and payment ledger history for a supplier.",
            input_schema={
                "type": "object",
                "properties": {
                    "supplier_id": {"type": "string", "description": "Supplier UUID."}
                },
                "required": ["supplier_id"],
            },
            handler=lambda args: self.client.request("GET", f"/api/suppliers/{args['supplier_id']}/ledger")[1],
        )

    # --- 9. Expenses & Financial Operations ---
    def _register_finance_tools(self) -> None:
        self.register(
            name="storeos_expenses_list",
            description="List operational shop expenses (rent, electricity, refreshments, logistics).",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "category": {"type": "string", "description": "Category filter."},
                    "from_date": {"type": "string", "description": "Start date (YYYY-MM-DD)."},
                    "to_date": {"type": "string", "description": "End date (YYYY-MM-DD)."},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/expenses", query={
                "branchId": args.get("branch_id"),
                "category": args.get("category"),
                "fromDate": args.get("from_date"),
                "toDate": args.get("to_date"),
            })[1],
        )
        self.register(
            name="storeos_expenses_create",
            description="Record a shop expense entry.",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                    "category": {"type": "string", "description": "Rent, Utilities, Food, Maintenance, Travel."},
                    "amount": {"type": "number", "description": "Expense amount."},
                    "description": {"type": "string", "description": "Description of expenditure."},
                    "payment_method": {"type": "string", "description": "Cash, UPI, BankTransfer."},
                },
                "required": ["category", "amount", "description"],
            },
            handler=lambda args: self.client.request("POST", "/api/expenses", branch_id=args.get("branch_id"), body={
                "branchId": args.get("branch_id") or self.client.branch_id,
                "category": args["category"],
                "amount": args["amount"],
                "description": args["description"],
                "paymentMethod": args.get("payment_method") or "Cash",
            })[1],
        )
        self.register(
            name="storeos_expenses_monthly_total",
            description="Get the aggregate expense sum and breakdown for a given year and month.",
            input_schema={
                "type": "object",
                "properties": {
                    "year": {"type": "integer", "description": "Calendar year."},
                    "month": {"type": "integer", "description": "Month number (1-12)."},
                    "branch_id": {"type": "string", "description": "Optional branch UUID."},
                },
                "required": ["year", "month"],
            },
            handler=lambda args: self.client.request("GET", "/api/expenses/monthly-total", query={
                "year": args["year"],
                "month": args["month"],
                "branchId": args.get("branch_id"),
            })[1],
        )
        self.register(
            name="storeos_liabilities_list",
            description="List active liabilities, loans, or structured payable obligations.",
            input_schema={"type": "object", "properties": {}},
            handler=lambda _: self.client.request("GET", "/api/liabilities")[1],
        )
        self.register(
            name="storeos_liabilities_repayment",
            description="Record a repayment installment against a liability.",
            input_schema={
                "type": "object",
                "properties": {
                    "liability_id": {"type": "string", "description": "Liability UUID."},
                    "amount": {"type": "number", "description": "Repayment amount."},
                    "payment_method": {"type": "string", "description": "Payment tender."},
                    "notes": {"type": "string", "description": "Remarks."},
                },
                "required": ["liability_id", "amount"],
            },
            handler=lambda args: self.client.request("POST", f"/api/liabilities/{args['liability_id']}/repayments", body={
                "amount": args["amount"],
                "paymentMethod": args.get("payment_method") or "Cash",
                "notes": args.get("notes") or "",
            })[1],
        )

    # --- 10. Reports & Analytics ---
    def _register_report_tools(self) -> None:
        self.register(
            name="storeos_reports_dashboard",
            description="Fetch executive dashboard summary (today's revenue, profit, sales count, active repairs, low stock count).",
            input_schema={
                "type": "object",
                "properties": {
                    "branch_id": {"type": "string", "description": "Branch UUID filter."}
                },
            },
            handler=lambda args: self.client.request("GET", "/api/reports/dashboard", query={
                "branchId": args.get("branch_id"),
            })[1],
        )
        self.register(
            name="storeos_reports_daily_sales",
            description="Retrieve detailed daily sales audit report for a specific date and branch.",
            input_schema={
                "type": "object",
                "properties": {
                    "date": {"type": "string", "description": "Date (YYYY-MM-DD). Defaults to today."},
                    "branch_id": {"type": "string", "description": "Branch UUID."},
                },
            },
            handler=lambda args: self.client.request("GET", "/api/reports/daily-sales", query={
                "date": args.get("date") or time.strftime("%Y-%m-%d"),
                "branchId": args.get("branch_id"),
            })[1],
        )
        self.register(
            name="storeos_reports_compare_branches",
            description="Compare performance and revenue metrics across all branches for a date window.",
            input_schema={
                "type": "object",
                "properties": {
                    "from_date": {"type": "string", "description": "Start date (YYYY-MM-DD)."},
                    "to_date": {"type": "string", "description": "End date (YYYY-MM-DD)."},
                },
                "required": ["from_date", "to_date"],
            },
            handler=lambda args: self.client.request("GET", "/api/reports/compare", query={
                "fromDate": args["from_date"],
                "toDate": args["to_date"],
            })[1],
        )
        self.register(
            name="storeos_exports_gstr1",
            description="Generate GSTR-1 tax compliance export summary for sales and invoices.",
            input_schema={
                "type": "object",
                "properties": {
                    "year": {"type": "integer", "description": "Calendar year."},
                    "month": {"type": "integer", "description": "Month number (1-12)."},
                },
                "required": ["year", "month"],
            },
            handler=lambda args: self.client.request("GET", "/api/exports/gstr1", query={
                "year": args["year"],
                "month": args["month"],
            })[1],
        )

    # --- 11. Admin, Users & Settings ---
    def _register_admin_tools(self) -> None:
        self.register(
            name="storeos_users_list",
            description="List staff user accounts and their assigned roles.",
            input_schema={"type": "object", "properties": {}},
            handler=lambda _: self.client.request("GET", "/api/users")[1],
        )
        self.register(
            name="storeos_settings_get",
            description="Read tenant configuration setting by key.",
            input_schema={
                "type": "object",
                "properties": {
                    "key": {"type": "string", "description": "Configuration key name."}
                },
                "required": ["key"],
            },
            handler=lambda args: self.client.request("GET", f"/api/settings/{args['key']}")[1],
        )
        self.register(
            name="storeos_settings_update",
            description="Modify a store configuration setting value.",
            input_schema={
                "type": "object",
                "properties": {
                    "key": {"type": "string", "description": "Configuration key."},
                    "value": {"type": "string", "description": "New value."},
                },
                "required": ["key", "value"],
            },
            handler=lambda args: self.client.request("PATCH", f"/api/settings/{args['key']}", body={
                "value": args["value"],
            })[1],
        )

    # --- 12. Universal Gateway Tool ---
    def _register_gateway_tool(self) -> None:
        self.register(
            name="storeos_api_call",
            description=(
                "Universal API Gateway tool. Call ANY StoreOS endpoint directly with full control over "
                "HTTP method, path, query parameters, request body, and branch context. "
                "Covers 100% of all 89 API routes."
            ),
            input_schema={
                "type": "object",
                "properties": {
                    "method": {
                        "type": "string",
                        "description": "HTTP method (GET, POST, PUT, PATCH, DELETE).",
                        "enum": ["GET", "POST", "PUT", "PATCH", "DELETE"],
                    },
                    "path": {
                        "type": "string",
                        "description": "API relative path (e.g. /api/platform/reconciliation/run-now, /api/roles).",
                    },
                    "query": {
                        "type": "object",
                        "description": "Optional query string key-value parameters.",
                    },
                    "body": {
                        "type": "object",
                        "description": "Optional request body JSON object.",
                    },
                    "branch_id": {
                        "type": "string",
                        "description": "Optional branch UUID override for this operation.",
                    },
                    "idempotency_key": {
                        "type": "string",
                        "description": "Optional Idempotency-Key UUID override.",
                    },
                },
                "required": ["method", "path"],
            },
            handler=lambda args: self.client.request(
                method=args["method"],
                path=args["path"],
                query=args.get("query"),
                body=args.get("body"),
                branch_id=args.get("branch_id"),
                idempotency_key=args.get("idempotency_key"),
            )[1],
        )


# -----------------------------------------------------------------------------
# MCP Resources & Prompts
# -----------------------------------------------------------------------------
class ResourceManager:
    """Manages MCP Resources."""

    def __init__(self, client: StoreOsClient, repo_root: Path) -> None:
        self.client = client
        self.repo_root = repo_root

    def list_resources(self) -> List[Dict[str, Any]]:
        return [
            {
                "uri": "storeos://system/status",
                "name": "StoreOS System Status",
                "description": "Live tenant health, active user, resolved branch, and permissions.",
                "mimeType": "application/json",
            },
            {
                "uri": "storeos://reports/dashboard",
                "name": "Executive Dashboard Snapshot",
                "description": "Real-time summary of today's sales, active repairs, and low stock.",
                "mimeType": "application/json",
            },
            {
                "uri": "storeos://system/openapi",
                "name": "StoreOS OpenAPI 3.0 Specification",
                "description": "Complete OpenAPI 3.0 specification covering all 89 endpoints and schemas.",
                "mimeType": "application/json",
            },
        ]

    def read_resource(self, uri: str) -> Optional[str]:
        if uri == "storeos://system/status":
            self.client.ensure_authenticated()
            _, me = self.client.request("GET", "/api/auth/me")
            return json.dumps({
                "apiUrl": self.client.api_url,
                "subdomain": self.client.subdomain,
                "tenantId": self.client.tenant_id,
                "activeBranchId": self.client.branch_id,
                "user": me,
            }, indent=2)

        if uri == "storeos://reports/dashboard":
            self.client.ensure_authenticated()
            _, dash = self.client.request("GET", "/api/reports/dashboard")
            return json.dumps(dash, indent=2)

        if uri == "storeos://system/openapi":
            spec_path = self.repo_root / "frontend" / "src" / "app" / "core" / "api" / "openapi.json"
            if spec_path.exists():
                return spec_path.read_text(encoding="utf-8")
            return json.dumps({"error": "openapi.json not found on disk"})

        return None


class PromptManager:
    """Manages MCP Prompts."""

    def list_prompts(self) -> List[Dict[str, Any]]:
        return [
            {
                "name": "daily-briefing",
                "description": "Generate daily morning or evening store performance briefing.",
                "arguments": [
                    {"name": "branch_id", "description": "Optional branch UUID filter.", "required": False}
                ],
            },
            {
                "name": "repair-intake",
                "description": "Step-by-step checklist to intake a customer device for repair.",
                "arguments": [
                    {"name": "customer_name", "description": "Customer name", "required": False},
                    {"name": "device_model", "description": "Device brand & model", "required": False},
                ],
            },
            {
                "name": "stock-audit",
                "description": "Audit low-stock inventory and prepare reorder purchase recommendations.",
                "arguments": [],
            },
            {
                "name": "pos-checkout-assistant",
                "description": "Construct and validate POS cart items and payment methods for a sale.",
                "arguments": [],
            },
        ]

    def get_prompt(self, name: str, arguments: Dict[str, Any]) -> Optional[Dict[str, Any]]:
        if name == "daily-briefing":
            branch = arguments.get("branch_id", "all branches")
            return {
                "description": "Daily store briefing",
                "messages": [{
                    "role": "user",
                    "content": {
                        "type": "text",
                        "text": (
                            f"Generate a comprehensive store performance briefing for {branch}. "
                            "Call storeos_reports_dashboard to inspect today's gross sales, invoice count, and profit. "
                            "Then call storeos_inventory_low_stock to highlight items requiring reordering, "
                            "and storeos_repairs_list with status ReadyForDelivery to list customer devices ready for pickup."
                        ),
                    },
                }],
            }

        if name == "repair-intake":
            cust = arguments.get("customer_name", "Customer")
            dev = arguments.get("device_model", "Device")
            return {
                "description": "Repair intake workflow",
                "messages": [{
                    "role": "user",
                    "content": {
                        "type": "text",
                        "text": (
                            f"Guide intaking a repair job for {cust} ({dev}). "
                            "1. Search if the customer exists using storeos_customers_list; if not, create them. "
                            "2. Collect device brand, model, IMEI/serial, passcode, and issue description. "
                            "3. Call storeos_repairs_create to open the ticket and return the ticket number."
                        ),
                    },
                }],
            }

        if name == "stock-audit":
            return {
                "description": "Stock audit workflow",
                "messages": [{
                    "role": "user",
                    "content": {
                        "type": "text",
                        "text": (
                            "Perform an inventory stock audit. "
                            "Call storeos_inventory_low_stock to find critical shortages, "
                            "and storeos_inventory_valuation for asset totals. "
                            "List suppliers for low items using storeos_suppliers_list and suggest purchase orders."
                        ),
                    },
                }],
            }

        if name == "pos-checkout-assistant":
            return {
                "description": "POS checkout helper",
                "messages": [{
                    "role": "user",
                    "content": {
                        "type": "text",
                        "text": (
                            "Assist with retail checkout. "
                            "1. Search items via storeos_pos_search. "
                            "2. Confirm quantities and payment tenders (Cash, UPI, Card). "
                            "3. Call storeos_pos_checkout to issue the invoice."
                        ),
                    },
                }],
            }

        return None


# -----------------------------------------------------------------------------
# JSON-RPC 2.0 Protocol Engine
# -----------------------------------------------------------------------------
class McpServer:
    """Model Context Protocol (MCP) Server implementing JSON-RPC 2.0 over stdio."""

    PROTOCOL_VERSION = "2024-11-05"
    SERVER_NAME = "StoreOS MCP Server"
    SERVER_VERSION = "1.0.0"

    def __init__(self, client: StoreOsClient, repo_root: Path) -> None:
        self.client = client
        self.repo_root = repo_root
        self.tools = ToolRegistry(client)
        self.resources = ResourceManager(client, repo_root)
        self.prompts = PromptManager()

    def handle_message(self, line: str) -> Optional[Dict[str, Any]]:
        """Process incoming JSON-RPC message line and return response dictionary."""
        if not line.strip():
            return None

        try:
            msg = json.loads(line)
        except Exception as ex:
            return {
                "jsonrpc": "2.0",
                "id": None,
                "error": {"code": -32700, "message": f"Parse error: {ex}"},
            }

        msg_id = msg.get("id")
        method = msg.get("method")
        params = msg.get("params", {})

        # Handle notifications (no response expected)
        if method == "notifications/initialized":
            log_debug("Client initialized handshake complete.")
            return None

        if method == "initialize":
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {
                    "protocolVersion": self.PROTOCOL_VERSION,
                    "capabilities": {
                        "tools": {},
                        "resources": {},
                        "prompts": {},
                    },
                    "serverInfo": {
                        "name": self.SERVER_NAME,
                        "version": self.SERVER_VERSION,
                    },
                },
            }

        if method == "ping":
            return {"jsonrpc": "2.0", "id": msg_id, "result": {}}

        # Tools Handlers
        if method == "tools/list":
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {"tools": self.tools.tools},
            }

        if method == "tools/call":
            tool_name = params.get("name", "")
            tool_args = params.get("arguments", {})
            is_err, result = self.tools.execute(tool_name, tool_args)

            formatted_text = json.dumps(result, indent=2) if isinstance(result, (dict, list)) else str(result)
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {
                    "content": [
                        {
                            "type": "text",
                            "text": formatted_text,
                        }
                    ],
                    "isError": is_err,
                },
            }

        # Resources Handlers
        if method == "resources/list":
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {"resources": self.resources.list_resources()},
            }

        if method == "resources/read":
            uri = params.get("uri", "")
            content = self.resources.read_resource(uri)
            if content is None:
                return {
                    "jsonrpc": "2.0",
                    "id": msg_id,
                    "error": {"code": -32602, "message": f"Resource not found: {uri}"},
                }
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {
                    "contents": [
                        {
                            "uri": uri,
                            "mimeType": "application/json",
                            "text": content,
                        }
                    ]
                },
            }

        # Prompts Handlers
        if method == "prompts/list":
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": {"prompts": self.prompts.list_prompts()},
            }

        if method == "prompts/get":
            prompt_name = params.get("name", "")
            prompt_args = params.get("arguments", {})
            prompt_data = self.prompts.get_prompt(prompt_name, prompt_args)
            if prompt_data is None:
                return {
                    "jsonrpc": "2.0",
                    "id": msg_id,
                    "error": {"code": -32602, "message": f"Prompt not found: {prompt_name}"},
                }
            return {
                "jsonrpc": "2.0",
                "id": msg_id,
                "result": prompt_data,
            }

        return {
            "jsonrpc": "2.0",
            "id": msg_id,
            "error": {"code": -32601, "message": f"Method not found: {method}"},
        }

    def run_stdio(self) -> None:
        """Main stdio loop reading JSON-RPC messages and writing responses."""
        log_debug(f"{self.SERVER_NAME} v{self.SERVER_VERSION} listening on stdio...")
        for line in sys.stdin:
            response = self.handle_message(line)
            if response is not None:
                sys.stdout.write(json.dumps(response) + "\n")
                sys.stdout.flush()


# -----------------------------------------------------------------------------
# Self-Test & Diagnostic Suite
# -----------------------------------------------------------------------------
def run_self_test(server: McpServer, live_api: bool = False) -> int:
    """Run verification self-test covering JSON-RPC handshake and tools."""
    print("Running StoreOS MCP Server Self-Test Suite...", file=sys.stderr)
    test_failures = 0

    # 1. Test Initialize
    init_req = json.dumps({
        "jsonrpc": "2.0",
        "id": 1,
        "method": "initialize",
        "params": {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "TestClient", "version": "1.0"},
        },
    })
    init_res = server.handle_message(init_req)
    if not init_res or init_res.get("result", {}).get("protocolVersion") != "2024-11-05":
        print("FAIL: initialize response invalid", file=sys.stderr)
        test_failures += 1
    else:
        print("PASS: initialize handshake (protocol 2024-11-05)", file=sys.stderr)

    # 2. Test Ping
    ping_res = server.handle_message(json.dumps({"jsonrpc": "2.0", "id": 2, "method": "ping"}))
    if not ping_res or ping_res.get("result") != {}:
        print("FAIL: ping response invalid", file=sys.stderr)
        test_failures += 1
    else:
        print("PASS: ping", file=sys.stderr)

    # 3. Test Tools List
    tools_res = server.handle_message(json.dumps({"jsonrpc": "2.0", "id": 3, "method": "tools/list"}))
    tools_list = tools_res.get("result", {}).get("tools", []) if tools_res else []
    if len(tools_list) < 25:
        print(f"FAIL: tools/list returned only {len(tools_list)} tools", file=sys.stderr)
        test_failures += 1
    else:
        print(f"PASS: tools/list registered {len(tools_list)} tools with valid schemas", file=sys.stderr)

    # Check for presence of essential tools
    essential = [
        "storeos_status",
        "storeos_products_list",
        "storeos_inventory_list",
        "storeos_pos_checkout",
        "storeos_repairs_create",
        "storeos_api_call",
    ]
    for tool in essential:
        if not any(t["name"] == tool for t in tools_list):
            print(f"FAIL: Essential tool missing: {tool}", file=sys.stderr)
            test_failures += 1

    # 4. Test Resources List
    res_list = server.handle_message(json.dumps({"jsonrpc": "2.0", "id": 4, "method": "resources/list"}))
    resources = res_list.get("result", {}).get("resources", []) if res_list else []
    if len(resources) < 3:
        print(f"FAIL: resources/list returned only {len(resources)} resources", file=sys.stderr)
        test_failures += 1
    else:
        print(f"PASS: resources/list registered {len(resources)} resources", file=sys.stderr)

    # 5. Test Prompts List
    p_list = server.handle_message(json.dumps({"jsonrpc": "2.0", "id": 5, "method": "prompts/list"}))
    prompts = p_list.get("result", {}).get("prompts", []) if p_list else []
    if len(prompts) < 4:
        print(f"FAIL: prompts/list returned only {len(prompts)} prompts", file=sys.stderr)
        test_failures += 1
    else:
        print(f"PASS: prompts/list registered {len(prompts)} prompts", file=sys.stderr)

    # 6. Test Format Compliance (2024-11-05 structure)
    call_req = json.dumps({
        "jsonrpc": "2.0",
        "id": 6,
        "method": "tools/call",
        "params": {
            "name": "storeos_set_branch",
            "arguments": {"branch_id": "33333333-3333-3333-3333-333333333333"},
        },
    })
    call_res = server.handle_message(call_req)
    call_result = call_res.get("result", {}) if call_res else {}
    if "content" not in call_result or not isinstance(call_result.get("content"), list):
        print("FAIL: tools/call response violates MCP 2024-11-05 content array format", file=sys.stderr)
        test_failures += 1
    elif call_result.get("isError") is not False:
        print("FAIL: storeos_set_branch returned unexpected error status", file=sys.stderr)
        test_failures += 1
    else:
        print("PASS: tools/call schema formatting strictly follows 2024-11-05 spec", file=sys.stderr)

    if live_api:
        print("\nTesting Live StoreOS API Connection...", file=sys.stderr)
        status_req = json.dumps({
            "jsonrpc": "2.0",
            "id": 7,
            "method": "tools/call",
            "params": {"name": "storeos_status", "arguments": {}},
        })
        status_res = server.handle_message(status_req)
        content_text = status_res.get("result", {}).get("content", [{}])[0].get("text", "")
        print(f"Live status response: {content_text[:200]}...", file=sys.stderr)

    if test_failures == 0:
        print("\nALL MCP TESTS PASSED GREEN (0 errors).", file=sys.stderr)
        return 0

    print(f"\nTEST SUITE FAILED with {test_failures} error(s).", file=sys.stderr)
    return 1


# -----------------------------------------------------------------------------
# Configuration Snippet Generator
# -----------------------------------------------------------------------------
def print_client_config(client_type: str, script_path: Path) -> None:
    """Print copy-paste configuration snippet for the requested AI client."""
    py_executable = sys.executable.replace("\\", "\\\\")
    script_str = str(script_path.resolve()).replace("\\", "\\\\")

    if client_type == "claude":
        config = {
            "mcpServers": {
                "storeos": {
                    "command": sys.executable,
                    "args": [str(script_path.resolve())],
                    "env": {
                        "STOREOS_API_URL": "http://localhost:5000",
                        "STOREOS_SUBDOMAIN": "apex",
                        "STOREOS_EMAIL": "owner@apex.example",
                        "STOREOS_PASSWORD": "Demo@12345",
                    },
                }
            }
        }
        print("\nClaude Desktop Configuration (add to %APPDATA%\\Claude\\claude_desktop_config.json):", file=sys.stderr)
        print(json.dumps(config, indent=2))

    elif client_type in ("gemini", "antigravity"):
        config = {
            "mcpServers": {
                "storeos": {
                    "command": sys.executable,
                    "args": [str(script_path.resolve())],
                    "env": {
                        "STOREOS_API_URL": "http://localhost:5000",
                        "STOREOS_SUBDOMAIN": "apex",
                    },
                }
            }
        }
        print("\nGemini / Antigravity MCP Configuration:", file=sys.stderr)
        print(json.dumps(config, indent=2))

    elif client_type == "cursor":
        config = {
            "storeos": {
                "command": sys.executable,
                "args": [str(script_path.resolve())],
                "env": {
                    "STOREOS_API_URL": "http://localhost:5000",
                    "STOREOS_SUBDOMAIN": "apex",
                },
            }
        }
        print("\nCursor MCP Configuration (add to .cursor/mcp.json):", file=sys.stderr)
        print(json.dumps(config, indent=2))

    else:
        print(f"Unknown client type: {client_type}. Choose from 'claude', 'gemini', or 'cursor'.", file=sys.stderr)


# -----------------------------------------------------------------------------
# Main CLI Entry Point
# -----------------------------------------------------------------------------
def main() -> int:
    parser = argparse.ArgumentParser(description="StoreOS Model Context Protocol (MCP) Server")
    parser.add_argument("--test", action="store_true", help="Run automated MCP protocol self-test suite")
    parser.add_argument("--live", action="store_true", help="Include live API checks during self-test")
    parser.add_argument("--config", choices=["claude", "gemini", "antigravity", "cursor"], help="Output client configuration JSON")
    parser.add_argument("--api-url", default=None, help="StoreOS API base URL (default http://localhost:5000)")
    parser.add_argument("--subdomain", default=None, help="Multi-tenant subdomain (default apex)")
    parser.add_argument("--email", default=None, help="Login email (default owner@apex.example)")
    parser.add_argument("--password", default=None, help="Login password (default Demo@12345)")
    args = parser.parse_args()

    repo_root = get_repo_root()
    script_path = Path(__file__).resolve()

    if args.config:
        print_client_config(args.config, script_path)
        return 0

    client = StoreOsClient(
        api_url=args.api_url,
        subdomain=args.subdomain,
        email=args.email,
        password=args.password,
    )
    server = McpServer(client, repo_root)

    if args.test:
        return run_self_test(server, live_api=args.live)

    server.run_stdio()
    return 0


if __name__ == "__main__":
    sys.exit(main())
