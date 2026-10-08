# Runbook: Tenant Provisioning CLI (`tools/provision`)

## 1. Overview & Architecture

Tenant provisioning in StoreOS creates a real, isolated tenant within the multi-tenant architecture. Because PostgreSQL Row Level Security (RLS) is active on all tenant tables (`tenants`, `branches`, `roles`, `role_permissions`, `users`, `user_branches`, `user_role_assignments`), and application connections (`storeos_app`) cannot bootstrap entities for a tenant that does not exist yet, tenant provisioning **always** runs using the privileged database owner role (`storeos_owner` / `storeos`), configured via `STOREOS_MIGRATIONS_CONNECTION`.

Tenant provisioning is strictly an operator CLI task. The provisioning tooling (`tools/provision`, `tools/StoreOS.Provisioning`) must **never** be included in the production API Docker image or exposed through HTTP endpoints (SaaS self-signup is out of scope).

---

## 2. CLI Usage

The provisioning tool is invoked using the .NET CLI:

```bash
dotnet run --project tools/provision -- \
  --name "Apex Mobiles" \
  --subdomain "apex" \
  --owner-email "owner@apex.example" \
  --owner-name "Anil Kumar" \
  --branch-code "B01" \
  --branch-name "Main Branch" \
  --gstin "27AAAAA0000A1Z5" \
  --timezone "Asia/Kolkata" \
  --repairs true
```

### CLI Options

| Option | Required | Default | Description |
|---|---|---|---|
| `--name`, `-n` | Yes | - | Display name of the tenant (e.g. `"Apex Mobiles"`). |
| `--subdomain`, `-s` | Yes | - | Tenant subdomain label matching `^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$`. |
| `--owner-email` | Yes | - | Valid email address for the initial Owner user. |
| `--owner-name` | Yes | - | Full name for the initial Owner user. |
| `--branch-code` | No | `B01` | Default branch code (1-3 uppercase alphanumeric characters, `^[A-Z1-9][A-Z0-9]{0,2}$`). |
| `--branch-name` | No | `Main Branch` | Default branch display name. |
| `--gstin` | No | `null` | 15-character GSTIN (`^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$`). |
| `--timezone` | No | `Asia/Kolkata` | IANA timezone identifier for business date computations. |
| `--repairs` | No | `false` | Enable/disable repairs module (`{"repairs": true|false}` in `features_json`). |
| `--owner-password` | No | Generated | Optional specific password; if omitted, a 16-character cryptographically secure password is generated. |
| `--connection`, `-c` | No | Env var / dev fallback | Database owner connection string (defaults to `STOREOS_MIGRATIONS_CONNECTION`). |

---

## 3. Provisioning Workflow

All actions occur within a **single database transaction**:

1. **Validation**:
   - Subdomain matches the lowercase alphanumeric slug regex `^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$` and is not currently in use.
   - Branch code matches `^[A-Z1-9][A-Z0-9]{0,2}$`.
   - Owner email and name are non-empty and valid.
   - GSTIN (if provided) matches the 15-character Indian GST format.
2. **Tenant & Branch Creation**:
   - Creates the `Tenant` entity with `features_json` containing `{"repairs": <flag>}`.
   - Creates the default `Branch` with the specified code, name, and GSTIN.
3. **Five Standard Roles**:
   - Creates the 5 baseline roles and their permissions exactly according to the matrix in `CONTRACTS.md` §7:
     - **Owner**: Tenant-wide, granted all 17 permissions (`PermissionCatalog.All`).
     - **RegionalManager**: Tenant-wide, granted `sales:read:any-branch`, `inventory:cost:view`, `reports:profit:view`.
     - **BranchManager**: Branch-scoped, granted `pos:checkout`, `sales:discount:apply`, `sales:credit:override`, `inventory:cost:view`, `reports:profit:view`, `inventory:manage`, `purchases:manage`, `inventory:adjustments`, `repairs:manage`.
     - **Cashier**: Branch-scoped, granted `pos:checkout`.
     - **Technician**: Branch-scoped, granted `repairs:manage`.
4. **Owner User Creation & Assignment**:
   - Generates a 16-character temporary password using `RandomNumberGenerator` (or hashes the provided password) via Argon2id.
   - Assigns the Owner role tenant-wide (`branch_id = NULL`).
   - Links the Owner user to the default branch with `is_default = true`.
5. **Audit Logging**:
   - Appends an audit log entry with action `tenant.provisioned`, entity `Tenant`, operator username, and correlation ID.
6. **Exit Codes & Output**:
   - **Exit 0**: Success. Prints the temporary password and login URL `https://<subdomain>.pos.storeos.io` once to `stdout`.
   - **Exit 1**: Validation or argument error.
   - **Exit 2**: Subdomain already exists (idempotent re-run failure).

---

## 4. Tenant Deactivation

To deactivate a tenant (e.g. for non-payment or account termination), an operator executes an `UPDATE` query directly via the owner connection:

```sql
UPDATE tenants
SET is_active = false
WHERE subdomain = '<subdomain>';
```

### Effect of Deactivation:
- **Login Blocked (P-303)**: The `/api/auth/login` endpoint validates `tenant.IsActive`. If `is_active` is `false`, login is blocked with HTTP 403 `tenant_inactive`.
- **Existing Tokens**: When access tokens expire (15-minute lifetime), refresh token exchange is blocked because tenant active status is re-evaluated.
- **Re-activation**: To re-activate the tenant:
  ```sql
  UPDATE tenants
  SET is_active = true
  WHERE subdomain = '<subdomain>';
  ```
