# StoreOS Demo Credentials & Tenant Info

This file contains the seeded tenant IDs and test account credentials for local development and QA.

---

## 🏢 Demo Tenant: Apex Mobiles Demo

| Field | Value |
|---|---|
| **Tenant Name** | `Apex Mobiles Demo` |
| **Subdomain** | `apex` |
| **Tenant ID** | `890082a5-ec33-4d3c-a57a-6e4beeb2b641` |

---

## 👥 Seeded User Accounts

> **Common Password for all demo accounts**: `Demo@12345`

| Role | Email | Password | Tenant ID | Access Scope |
|---|---|---|---|---|
| **Owner** | `owner@apex.example` | `Demo@12345` | `890082a5-ec33-4d3c-a57a-6e4beeb2b641` | Full Access (All Branches) |
| **Branch Manager** | `manager@apex.example` | `Demo@12345` | `890082a5-ec33-4d3c-a57a-6e4beeb2b641` | Branch B01 / B02 |
| **Cashier** | `cashier@apex.example` | `Demo@12345` | `890082a5-ec33-4d3c-a57a-6e4beeb2b641` | POS & Sales |
| **Technician** | `tech@apex.example` | `Demo@12345` | `890082a5-ec33-4d3c-a57a-6e4beeb2b641` | Repairs |

---

## 🏪 Branches

| Code | Branch Name | Address |
|---|---|---|
| **B01** | Main Branch | Lamington Road, Mumbai |
| **B02** | Mall Branch | Phoenix Mall, Lower Parel, Mumbai |

---

## 💡 How to Log In via Web UI

1. Navigate to `/login` (e.g., `http://localhost:4200/login`).
2. Enter **Email Address**: `owner@apex.example`
3. Enter **Password**: `Demo@12345`
4. Enter **Tenant ID**: `890082a5-ec33-4d3c-a57a-6e4beeb2b641`
5. Click **Sign In**.
