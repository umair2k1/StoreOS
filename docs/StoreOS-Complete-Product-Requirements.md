# Phone Zone — Complete Product Requirements & Behavioral Specification

**Document type:** Product / Functional Requirements Specification  
**Purpose:** Handover / independent reimplementation  
**Baseline:** Current Phone Zone codebase and database model reviewed in September 2026  
**Target:** A functionally equivalent application with freedom to use a different UI, visual language, framework, or technology stack.

---

## 1. Executive Summary

Phone Zone is an **offline-first desktop Point-of-Sale (POS), inventory, customer ledger, supplier/payables, repair-management, expense, reporting, and business-audit application** designed for a mobile-phone retail shop.

The product's core value is not its visual design. The essential product behavior is:

1. Maintain a structured catalogue of phones, used phones, accessories, and repair services.
2. Track physical stock and purchase history.
3. Track individual phone IMEIs where applicable.
4. Sell products and services through a POS workflow.
5. Calculate Indian GST correctly, including a used-phone margin scheme.
6. Generate invoices/estimates and support printing.
7. Support cash, UPI, card, credit, and financed/EMI sales.
8. Track customer purchase history and outstanding credit.
9. Record subsequent customer payments against unpaid invoices.
10. Track supplier balances, supplier transactions, and supplier returns.
11. Track business loans/liabilities and repayments.
12. Manage repair-service definitions and repair-job tickets through a lifecycle.
13. Record operational expenses.
14. Provide dashboards, sales audit reports, inventory valuation, and exports.
15. Provide backup/restore and data portability.
16. Operate locally without requiring a cloud backend.
17. Protect the application using application-level security/licensing controls.
18. Preserve transactional history rather than silently overwriting financial records.

A replacement implementation may look completely different, but if it satisfies this document, its **backend/domain behavior should be substantially equivalent** to Phone Zone.

---

# 2. Product Scope

## 2.1 Primary users

### Shop Owner / Administrator

Requires:

- Full inventory access.
- Purchase and supplier management.
- Sales and invoice access.
- Customer ledger.
- Expenses.
- Profit and business reporting.
- Liabilities.
- Repairs.
- Settings.
- Backup/export.
- Security/licensing controls.

### Cashier / Sales Staff

Requires:

- Fast product lookup.
- Barcode scanning.
- Cart management.
- Customer lookup.
- Billing.
- Payment capture.
- Invoice printing.
- Stock availability.

Sensitive financial information such as purchase price, profit, expenses, and owner settings may be restricted in a multi-user implementation.

### Repair Technician

If the repair module is enabled:

- View repair jobs.
- Update repair status.
- Record diagnosis.
- Record technician notes.
- Record parts used.
- Record approval.
- Mark jobs ready for pickup.
- Complete/deliver jobs.

---

# 3. Core Product Principles

## 3.1 Offline-first

The application must function without an internet connection for its normal business operations.

Internet connectivity must not be a prerequisite for:

- POS billing.
- Inventory.
- Customers.
- Expenses.
- Repairs.
- Reports.
- Local printing.
- Local backup.

Optional remote/multi-device functionality may require a network connection.

## 3.2 Local source of truth

The local database is the authoritative business datastore in standalone mode.

## 3.3 Financial history must be auditable

The system must not destroy historical information simply to make the current balance look correct.

Examples:

- Customer repayments must be separate transactions.
- Liability repayments must be separate transactions.
- Sales voids/refunds must preserve the original invoice.
- Supplier returns must have their own ledger.
- Sale-line information should retain historical snapshots such as item name, HSN, purchase price, and selling price.

## 3.4 Physical inventory and service inventory are different concepts

A phone/accessory is physical stock.

A repair service is a sellable service definition and must **not** behave like a physical inventory unit.

Therefore repair services must not inflate:

- Stock quantity.
- Stock cost.
- Retail inventory valuation.
- Low-stock counts.
- Out-of-stock counts.

---

# 4. Application Modules

The application consists conceptually of:

1. Dashboard
2. Point of Sale / Billing
3. Inventory
4. Sales History / Audit
5. Customers
6. Expenses
7. Suppliers / Payables
8. Loans & Liabilities
9. Repairs
10. Reports / Exports
11. Settings
12. Backup / Restore
13. Security / Licensing
14. Optional Multi-Device / Remote Access

---

# 5. Dashboard Requirements

## 5.1 Purpose

Provide an at-a-glance operational and financial overview.

## 5.2 Required metrics

### Today's revenue

Sum of active tax invoices created today.

Exclude:

- Voided sales.
- Refunded sales.
- Estimates.

### Today's invoice count

Count of active tax invoices created today.

### Monthly profit indicator

Conceptually:

```text
Monthly Profit =
Revenue
- Cost of Goods Sold
- Operational Expenses
```

The implementation must clearly define the source of COGS and not accidentally use current inventory purchase prices for historical sales when historical purchase cost snapshots are available.

### Active repairs

When repair module is enabled:

```text
Active Repairs =
repair jobs whose status is neither Delivered nor Cancelled
```

Also display:

```text
Ready for Pickup =
repair jobs with status "Ready for Pickup"
```

## 5.3 Low-stock alerts

Physical inventory items at or below the low-stock rule must be highlighted.

The current implementation uses a practical threshold of **3 units** for dashboard low-stock detection.

Repair services must be excluded.

## 5.4 Top-selling items

Display the top five physical/service sale-line names by quantity for the current month, excluding complimentary gift lines.

## 5.5 Seven-day revenue chart

Display daily active tax-invoice revenue for the last seven days.

The exact chart technology is not a requirement.

## 5.6 Quick actions

The dashboard should provide direct navigation to:

- New Sale.
- Add Stock.
- Sales.
- Inventory.
- Reports.
- Repairs where enabled.

## 5.7 Daily report action

The dashboard should provide a way to generate/print the daily sales report.

---

# 6. Point of Sale / Billing Requirements

The POS is one of the most critical workflows.

## 6.1 Product search

Users must be able to search inventory rapidly.

Search should support normal product-name lookup and barcode/scanner input.

## 6.2 Barcode scanner behavior

A hardware barcode scanner should be usable as a keyboard input device.

The application should buffer scanner input sufficiently to avoid interpreting rapid scanner characters as ordinary UI typing.

## 6.3 Product categories

Physical products:

- New Phone
- Used Phone
- Accessory

Services:

- Repair Service

## 6.4 Add-to-cart rules

For physical inventory:

- Cannot add an item with zero or negative stock.
- Adding an existing item increases its quantity.
- Quantity cannot exceed available stock.

For repair services:

- Stock validation does not apply.
- Quantity can be billed as a service quantity.

Each cart row must preserve:

- Item identity.
- Item name.
- Category.
- Quantity.
- Unit selling price.
- GST rate.
- Purchase price where relevant.
- HSN/SAC.
- Margin-scheme state.
- IMEI where applicable.

## 6.5 Customer selection

The POS must support:

- Walk-in customer.
- Existing customer search.
- Customer name.
- Customer phone.
- Customer GSTIN.

Returning customers should be quickly selectable.

## 6.6 Walk-in customer

Default customer:

```text
Walk-in Customer
```

## 6.7 Cart editing

The cashier must be able to:

- Increase quantity.
- Decrease quantity.
- Remove a line.
- Change allowed price/discount fields where authorized.
- Enter IMEI.
- Toggle margin scheme where applicable.
- Add gifts.
- Apply invoice-level discount.

## 6.8 Gift/complimentary items

The POS supports complimentary items.

Gift lines must be distinguishable from normal billed items.

Gift lines must not be treated as normal sales revenue in reports that explicitly exclude gifts.

The invoice can retain gift information.

---

# 7. GST and Tax Engine

## 7.1 Standard GST

Prices are treated as tax-inclusive.

For a line:

```text
taxableBase = unitPrice / (1 + gstRate / 100)
totalGST = unitPrice - taxableBase
CGST = totalGST / 2
SGST = totalGST / 2
```

Round monetary values to two decimal places.

## 7.2 Margin Scheme

Margin scheme is permitted only for **Used Phone** items.

For:

```text
selling price = S
purchase price = P
GST rate = R
```

**Cost Source Rule for Margin Scheme:**
For used phones tracked by IMEI, purchase price $P$ MUST be the specific acquisition cost of that exact physical unit (`inventory_imeis.purchase_price` recorded when the unit was purchased/stocked-in), NOT a generic catalog-wide default `products.purchase_price`. If sold without an IMEI, $P$ falls back to the branch inventory cost basis.

If:

```text
S - P <= 0
```

then:

```text
taxableBase = 0
CGST = 0
SGST = 0
```

Otherwise:

```text
margin = S - P
taxableMargin = margin / (1 + R/100)
totalGST = margin - taxableMargin

CGST = totalGST / 2
SGST = totalGST / 2
```

## 7.3 Discount handling

Invoice discount must:

- Never become negative.
- Never exceed subtotal.
- Reduce the effective taxable base and GST proportionally.

Conceptually:

```text
grandTotal = subtotal - discount

factor = grandTotal / subtotal

taxable = taxableBeforeDiscount * factor
CGST = CGSTBeforeDiscount * factor
SGST = SGSTBeforeDiscount * factor
```

## 7.4 Tax configuration

Default GST rate should be configurable in settings.

Current default:

```text
18%
```

---

# 8. Invoice Requirements

## 8.1 Invoice types

Support:

- Tax Invoice
- Estimate

An estimate must not behave like a finalized taxable sale for reporting purposes.

### Estimate to Invoice Conversion
The system must provide an automated workflow to convert an accepted Estimate into a finalized Tax Invoice without requiring cashiers to rebuild the cart. The conversion action:
1. Validates that all physical items are currently in stock at the branch and selected IMEIs remain available.
2. Prompts for customer payment details and validates customer credit limits.
3. Allocates a new Tax Invoice number and applies inventory decrements and ledger postings atomically.
4. Marks the source estimate as converted with a bidirectional reference to the generated tax invoice.

## 8.2 Invoice number

Invoice numbers must be unique.

### Formats & GST Rule 46(b) Compliance
Rule 46(b) of the CGST Rules mandates a *consecutive serial number, not exceeding sixteen characters, in one or multiple series, unique for a financial year*.
- **Financial Year Consecutive Series (Recommended):** `<BRANCH>/<FY>/<NNNNNN>` (e.g. `B01/26-27/000042`, <= 16 characters, strictly consecutive across the financial year).
- **Daily Timestamp Series (POS format):** `YYYYMMDD-HHMMSS-NNN` or `<BRANCH>-YYYYMMDD-HHMMSS-NNN` (consecutive within a single day).

*CA Confirmation Requirement:* Because daily-reset counters may challenge strict statutory financial-year consecutive seriality under audit, the sequence engine must support configurable series formats, and the series pattern must be explicitly confirmed with the business's Chartered Accountant (CA).

The implementation must guarantee uniqueness even under rapid or concurrent creation.

## 8.3 Invoice contents

A tax invoice should be capable of showing:

- Shop name.
- Address.
- Phone.
- Email.
- GSTIN.
- Invoice number.
- Date/time.
- Customer name.
- Customer GSTIN.
- Product/service name.
- HSN/SAC.
- Quantity.
- Unit price.
- Discount.
- Taxable amount.
- CGST.
- SGST.
- Total.
- Payment information.
- IMEI where applicable.
- Finance/EMI information where applicable.
- Gift information where applicable.
- Description/notes where applicable.
- **E-Invoicing Fields (mandatory once aggregate PAN turnover crosses ₹5 crore):**
  - IRN (Invoice Reference Number — 64-character hash).
  - IRP Acknowledgment Number and Date.
  - Signed B2B QR Code.

## 8.4 Printing

Invoice printing must support the configured shop branding and printer/page format.

The visual layout is replaceable; the information content is not.

---

# 9. Payments and Credit

## 9.1 Supported payment modes

At minimum:

- Cash
- UPI
- Card
- Credit

## 9.2 Partial payments

An invoice can be partially paid.

The system must track:

```text
Grand Total
Amount Paid
Outstanding Balance
```

Outstanding:

```text
balance = max(0, grandTotal - amountPaid)
```

## 9.3 Payment audit log

Every subsequent customer payment must be stored as a separate transaction.

Required data:

- Sale/invoice ID.
- Amount.
- Payment mode.
- Payment date.

Do not simply overwrite the original invoice's payment amount when recording a later installment.

## 9.4 Payment validation

A payment must not exceed the remaining balance unless the product explicitly supports overpayments.

---

# 10. Financing / EMI

The POS supports financed sales.

## 10.1 Finance fields

Support:

- Finance provider.
- Financer name.
- Down payment.
- EMI details.
- Tenure in months.
- Annual interest rate.
- Calculated EMI.
- Total payable.

## 10.2 EMI calculation

For zero interest:

```text
EMI = principal / months
```

For positive annual interest:

```text
monthlyRate = annualRate / 12 / 100

EMI =
P * r * (1+r)^n
----------------
(1+r)^n - 1
```

Where:

- `P` = financed principal.
- `r` = monthly rate.
- `n` = number of months.

Principal:

```text
principal = grandTotal - downPayment
```

## 10.3 Finance provider memory

Previously entered finance providers should be remembered.

The provider list must:

- Normalize names.
- Deduplicate case-insensitively.
- Allow adding providers.
- Allow removing providers from the suggestion list.
- Never modify historical sales when a provider is removed.

No AI is required.

---

# 11. Inventory Requirements

## 11.1 Inventory master

Each physical inventory SKU should support:

- Name.
- Category.
- Stock quantity.
- Purchase price.
- Selling price.
- GST rate.
- Margin-scheme flag.
- HSN code.
- Supplier information.
- Seller information.

## 11.2 Categories

Required:

```text
New Phone
Used Phone
Accessory
Repair Service
```

## 11.3 Inventory list

Users must be able to:

- View items.
- Search items.
- Filter by category.
- Filter by stock status.
- Filter by tax status.
- Filter by margin scheme.
- Sort.
- Paginate.
- Select multiple records where bulk actions are supported.

## 11.4 Stock states

At minimum:

- In stock.
- Low stock.
- Out of stock.

The current dashboard/reporting rule treats:

```text
qty <= 3
```

as low stock for physical inventory.

Out of stock:

```text
qty <= 0
```

## 11.5 Inventory valuation

For physical stock:

```text
Cost valuation = Σ(qty × purchasePrice)

Retail valuation = Σ(qty × sellPrice)

Potential gross value =
retail valuation - cost valuation
```

Repair services are excluded.

## 11.6 Inventory unit IMEI tracking

Phones may require unit-level IMEI tracking.

The system must support:

- IMEI capture on purchase/stock-in.
- IMEI status.
- IMEI search.
- IMEI sale association.
- IMEI supplier-return association.

IMEI states:

- In Stock
- Sold
- Returned to Supplier

## 11.7 IMEI uniqueness

The system must prevent a single IMEI from being simultaneously represented as two active inventory units.

At sale time, the IMEI must be verified as available.

---

# 12. Purchase / Stock-In Requirements

## 12.1 Purchase ledger

Every stock-in event should be traceable to a purchase record.

Purchase record:

- Item.
- Quantity.
- Purchase rate.
- Supplier.
- Purchase date.
- Seller information.
- Supplier contact information.

## 12.2 Supplier information

Support:

- Supplier name.
- Phone.
- Address.

## 12.3 Stock update

When a physical product is purchased:

```text
stockQty += purchasedQty
```

If IMEIs are supplied:

- Create one unit-level IMEI record per physical phone.
- Associate each IMEI with the purchase.
- Mark status `In Stock`.

## 12.4 Supplier invoice/import capability

Where supplier invoices are imported, the system should:

1. Read structured invoice data.
2. Identify supplier.
3. Match or create inventory products.
4. Record purchase quantities and rates.
5. Record supplier invoice information where supported.
6. Capture IMEIs for phone units.
7. Avoid creating duplicate stock when the same invoice is imported twice.
8. Require user confirmation for ambiguous product mappings.
9. Never use AI as a hidden dependency.

If a supplier document cannot be parsed deterministically, it should be rejected or routed to a manual mapping step rather than guessed.

---

# 13. Supplier Management

## 13.1 Supplier master

Supplier fields:

- Name.
- Phone.
- Address.
- Brand/category.
- Notes.
- Created timestamp.

## 13.2 Supplier transaction types

Support:

- Bill.
- Payment.
- Opening Balance.

## 13.3 Supplier transaction fields

- Supplier.
- Type.
- Amount.
- Payment mode.
- Reference number.
- Transaction date.
- Notes.

Payment modes include:

- Cash.
- UPI.
- Bank Transfer.
- Cheque.
- Credit.

## 13.4 Supplier payable

The supplier balance should be derived from supplier transactions rather than being an un-audited manually editable number.

Conceptually:

```text
Payable =
Opening Balance
+ Bills
- Payments
- Applicable Credits/Returns
```

---

# 14. Supplier Returns

## 14.1 Purpose

Allow stock to be returned to a supplier while preserving an audit trail.

## 14.2 Return header

Required:

- Unique return number.
- Return date.
- Supplier.
- Supplier phone.
- Supplier address.
- Total quantity.
- Total amount.
- Settlement type.
- Reason.
- Notes.

## 14.3 Settlement types

- Payable Deduction.
- Cash/Bank Refund.
- Pending Credit.

## 14.4 Return lines

Each line should support:

- Item.
- Quantity.
- Purchase price.
- Return rate.
- Line total.
- IMEI numbers where applicable.

## 14.5 Inventory effect

Returning physical stock must reduce available stock.

For IMEI-tracked units:

```text
IMEI status = Returned to Supplier
```

The returned IMEI must no longer be sellable.

---

# 15. Sales Returns, Voids, and Refunds

## 15.1 Sales history states

Sales support:

- Active.
- Voided.
- Refunded.

## 15.2 Void

Voiding a sale must preserve the original sale record.

It should not delete the invoice from history.

The system should:

- Change sale status.
- Reverse appropriate inventory effects.
- Preserve original invoice information.
- Preserve auditability.

## 15.3 Refund

Refund processing must preserve the original sale.

The system should:

- Mark the appropriate sale/line as refunded according to business rules.
- Return eligible inventory.
- Restore IMEI availability when a physical tracked unit is returned to stock.
- Preserve refund history.

## 15.4 IMEI return behavior

A returned phone's IMEI must move from:

```text
Sold
```

back to an appropriate available state if the business rules permit resale.

---

# 16. Sales History / Audit Screen

## 16.1 Purpose

Provide a searchable audit ledger of invoices.

## 16.2 Universal search

Search should match:

- Invoice number.
- Customer name.
- Customer GSTIN.
- Item names.
- IMEIs.
- Finance provider.
- Financer name.
- Description.
- Gift information.

## 16.3 Date filters

Support:

- All.
- Today.
- Yesterday.
- This week.
- This month.
- Last month.
- This year.
- Custom range.

## 16.4 Filters

Support:

- Status.
- Payment mode.
- Invoice type.
- Paid vs due.
- Financed vs standard.
- Product category.
- Minimum amount.
- Maximum amount.

## 16.5 Sorting

Support sorting by:

- Sale date.
- Grand total.
- Invoice number.
- Customer name.
- Outstanding balance.

Ascending and descending ordering must be supported.

## 16.6 Pagination

Support configurable page size such as:

- 25.
- 50.
- 100.
- All.

## 16.7 Invoice details

Clicking a sale should open a detailed view showing:

- Header.
- Customer.
- Line items.
- Taxes.
- Payment.
- Balance.
- IMEI.
- Financing.
- Status.
- Notes.

## 16.8 Actions

Depending on sale state and permissions:

- Print invoice.
- Record payment.
- Void.
- Refund.
- View details.

---

# 17. Customer Ledger

## 17.1 Customer master

Fields:

- Name.
- Phone.
- GSTIN.
- Total purchases.
- Created date.

## 17.2 Customer list

Display:

- Name.
- Phone.
- Total purchases.
- Current credit balance.

## 17.3 Customer search

Search by:

- Name.
- Phone.

## 17.4 Customer history

Customer detail view should show:

- Sales invoices.
- Total.
- Paid amount.
- Due amount.
- Invoice status.
- Payment mode.
- Repair tickets when repair module is enabled.

## 17.5 Customer credit balance & credit limits

For active tax invoices:

```text
creditBalance =
Σ(max(0, grandTotal - amountPaid))
```

Estimates must not create customer credit.

### Credit Limits & Cross-Branch Visibility
- **Cross-branch exposure:** In multi-branch operations, customer credit is tenant-global. A balance incurred at Branch A is visible and enforceable at Branch B.
- **Credit Limit ($L$):** The system must allow assigning an optional credit limit to each customer.
- **POS Checkout Check:** The POS checkout interface must display the customer's live consolidated credit balance. If `creditBalance + newCreditAmount > creditLimit`, the checkout workflow must display a credit limit alert and block the transaction unless approved via manager override permission.

## 17.6 Customer payment

For an unpaid invoice:

- Show remaining due.
- Allow recording payment.
- Select payment mode.
- Save a payment transaction.
- Recalculate outstanding balance.

---

# 18. Expense Tracker

## 18.1 Purpose

Track operational business expenses.

## 18.2 Expense fields

- Description.
- Amount.
- Category.
- Date.
- Notes.

## 18.3 Categories

Current standard categories:

- Rent.
- Utilities.
- Salaries.
- Miscellaneous.

The design should permit future categories.

## 18.4 Validation

- Description required.
- Amount required.
- Amount must be greater than zero.
- Date required.

## 18.5 Expense history

Support date-range filtering.

Default view:

- First day of current month.
- Current date.

## 18.6 Monthly total

Show total expenses for the current month.

---

# 19. Loans & Liabilities

## 19.1 Purpose

Track business financial obligations outside supplier payables.

## 19.2 Liability types

- Loan.
- Credit Card.
- Financing.
- Investment / CapEx.

## 19.3 Liability fields

- Title.
- Type.
- Lender name.
- Total principal.
- Current balance.
- Due date.
- Status.
- Notes.
- Created date.

## 19.4 Repayment ledger

Repayments must be separate records.

Fields:

- Liability ID.
- Amount.
- Payment mode.
- Payment date.
- Notes.

## 19.5 Balance

Preferred calculation:

```text
Current Balance =
Original Principal
- Sum(Repayments)
```

If a manually maintained balance exists for migration compatibility, it must reconcile with transaction history.

## 19.6 Editing

The user may edit descriptive/master data.

Editing must not rewrite repayment history.

## 19.7 Repayment

Recording a repayment must create a new transaction.

Do not represent a repayment by silently reducing the original principal.

---

# 20. Repair Service Module

The repair module is feature-flagged and can be enabled/disabled.

## 20.1 Service catalogue

A repair service definition supports:

- Name.
- Description.
- Standard charge.
- Taxable flag.
- GST rate.
- SAC code.
- Estimated duration.
- Warranty days.
- Active flag.
- Created date.
- Updated date.

Default SAC:

```text
998713
```

Default GST:

```text
18%
```

Default duration:

```text
60 minutes
```

Default warranty:

```text
30 days
```

## 20.2 Service lifecycle

Services can be:

- Active.
- Inactive.

Inactive services should not normally appear in new ticket/POS selection.

Historical usage must remain intact.

---

# 21. Repair Job Ticketing

## 21.1 Job number

Format:

```text
REP-YYYYMMDD-XXXX
```

The sequence should increment per day.

## 21.2 Customer information

Repair job supports:

- Customer ID.
- Customer name.
- Phone.
- Address.

## 21.3 Device information

Support:

- Brand.
- Model.
- Device type.
- Serial number.
- IMEI 1.
- IMEI 2.

## 21.4 Intake information

Capture:

- Device condition.
- Accessories received.
- Problem description.
- Customer notes.

## 21.5 Diagnosis and internal information

Capture:

- Diagnosis.
- Technician notes.
- Internal notes.
- Assigned technician.

## 21.6 Commercial information

Support:

- Estimated cost.
- Approved cost.
- Parts cost.
- Labor charge.
- Discount.
- Tax.
- Final total.
- Advance paid.
- Balance due.

## 21.7 Customer approval

A repair may require explicit customer approval.

Store:

- Approval state.
- Approval timestamp.

## 21.8 Expected completion

Store expected completion date/time.

## 21.9 Warranty

Store:

```text
warranty_until
```

based on the configured service/job warranty policy.

---

# 22. Repair Status Lifecycle

Supported states:

```text
Received
Diagnosing
Waiting for Approval
Waiting for Parts
In Repair
Ready for Pickup
Delivered
Cancelled
```

## 22.1 Status history

Every status transition must be auditable.

Store:

- Repair job.
- Previous status.
- New status.
- Notes.
- Changed by.
- Timestamp.

## 22.2 Active repair definition

Active:

```text
status NOT IN ('Delivered', 'Cancelled')
```

## 22.3 Search

Repair search should support:

- Job number.
- Customer name.
- Customer phone.
- Device model.
- Serial number.
- IMEI 1.

## 22.4 Status filtering

Support:

- All.
- Active.
- Individual statuses.

## 22.5 Parts consumption

A repair can consume inventory parts.

Each part record stores:

- Repair job.
- Item.
- Part name.
- Quantity.
- Unit cost.
- Customer charge.
- Status.

Part statuses:

- Reserved.
- Consumed.
- Returned.

Physical inventory must be adjusted appropriately when parts are consumed or returned.

---

# 23. Repair Billing

A completed repair may result in an invoice.

The repair system must be capable of:

- Generating a sales invoice number.
- Linking repair job to sale.
- Linking invoice number to repair job.
- Billing service/labor/parts.
- Recording payment.
- Printing the repair invoice/ticket.

---

# 24. Reporting Requirements

The reporting subsystem is a core business feature.

## 24.1 Daily Sales Audit Report

Required information:

### Sales summary

- Revenue.
- Buying cost.
- Profit.
- GST collected.
- Invoice count.
- Items sold.

### Inventory summary

- Total stock volume.
- Stock worth at cost.
- Retail valuation.
- Low-stock item count.
- Out-of-stock item count.

Repair services must be excluded from physical inventory metrics.

### Payment breakdown

At minimum:

- Cash.
- UPI.
- Finance.
- Other.
- Total.

The report must clearly distinguish:

- Sale value.
- Collected amount.
- Outstanding amount.

### Sales by category

At minimum:

- New Phone.
- Used Phone.
- Accessory.
- Repair Service.

### Itemized sales log

Include enough detail for audit:

- Invoice.
- Date.
- Customer.
- Item.
- Quantity.
- Selling value.
- Purchase/cost value.
- GST.
- IMEI where applicable.
- Payment mode.

---

# 25. Inventory Reports

Inventory reporting should support:

- Current stock.
- Cost valuation.
- Retail valuation.
- Profit potential.
- Category.
- Supplier.
- Stock status.
- IMEI count.
- Physical phone inventory.

Repair services require a separate service-oriented presentation.

---

# 26. Inventory Export

Support export to spreadsheet and/or print.

The export should be able to include:

- Item.
- Category.
- Quantity.
- Purchase price.
- Selling price.
- GST.
- HSN.
- Supplier.
- IMEI information where appropriate.

## 26.1 Multi-category export

The user should be able to select multiple categories.

Example:

```text
New Phones
Used Phones
Accessories
Repair Services
```

## 26.2 Repair services

Repair services should not be forced into physical-inventory columns.

Use a service-specific representation:

- Service.
- Description.
- Service charge.
- Taxable.
- GST.
- SAC.

---

# 27. GSTR-1 Export

The application must support export of active tax invoices in a GSTR-1-compatible spreadsheet representation for a selected date range.

At minimum, exported records need sufficient information for:

- Invoice number.
- Date.
- Customer.
- Customer GSTIN.
- Taxable value.
- CGST.
- SGST.
- Total invoice value.
- HSN/SAC.
- Item/category information as applicable.

Only eligible active tax invoices should be included.

---

# 28. Backup and Restore

## 28.1 JSON backup

Provide a complete application-data export to a timestamped JSON file.

The backup should contain all important business tables/data required to restore the business state.

## 28.2 Restore

Restore must:

1. Validate file structure.
2. Validate supported schema/data version.
3. Preserve referential integrity.
4. Avoid partial imports.
5. Provide a clear error if the backup is invalid.

## 28.3 Backup safety

A restore should be transactional or use a staging database.

Never partially overwrite a live database after a failed restore.

---

# 29. Database Requirements

The logical model includes the following entities.

## 29.1 Settings

```text
settings
- key
- value
```

Key/value settings support shop configuration and feature-specific preferences.

## 29.2 Items

```text
items
- id
- name
- category
- stock_qty
- purchase_price
- sell_price
- gst_rate
- is_margin_scheme
- hsn_code
- seller_name
- seller_phone
- seller_address
- supplier_name
- supplier_phone
- supplier_address
```

## 29.3 Purchases

```text
purchases
- id
- item_id
- qty
- purchase_rate
- supplier_name
- purchase_date
- seller information
- supplier contact information
```

## 29.4 Sales

```text
sales
- id
- invoice_number
- sale_date
- customer_name
- customer_gstin
- total_taxable
- total_cgst
- total_sgst
- grand_total
- amount_paid
- payment_mode
- status
- invoice_type
- discount
- down_payment
- emi_details
- finance_provider
- financer_name
- gift_details
- description
```

## 29.5 Sale Items

```text
sale_items
- id
- sale_id
- item_id
- item_name snapshot
- qty
- price_per_unit
- margin flag
- cgst
- sgst
- imei
- HSN snapshot
- purchase price snapshot
- gift flag
```

The snapshot fields are intentional: historical invoices must remain understandable even if the master item changes later.

## 29.6 Customers

```text
customers
- id
- name
- phone
- gstin
- total_purchases
- created_at
```

## 29.7 Customer Payments

```text
customer_payments
- id
- sale_id
- amount
- payment_mode
- payment_date
```

## 29.8 Expenses

```text
expenses
- id
- description
- amount
- category
- expense_date
- notes
```

## 29.9 Suppliers

```text
suppliers
- id
- name
- phone
- address
- brand_or_category
- notes
- created_at
```

## 29.10 Supplier Transactions

```text
supplier_transactions
- id
- supplier_id
- type
- amount
- payment_mode
- reference_no
- txn_date
- notes
```

## 29.11 Liabilities

```text
liabilities
- id
- title
- type
- lender_name
- total_principal
- current_balance
- due_date
- status
- notes
- created_at
```

## 29.12 Liability Payments

```text
liability_payments
- id
- liability_id
- amount
- payment_mode
- payment_date
- notes
```

## 29.13 Inventory IMEIs

```text
inventory_imeis
- id
- item_id
- purchase_id
- imei
- status
- purchase_date
- purchase_price
- supplier information
- sale_id
- sale_item_id
- return_id
- created_at
```

## 29.14 Supplier Returns

```text
supplier_returns
- id
- return_number
- return_date
- supplier_id
- supplier information
- total_qty
- total_amount
- settlement_type
- reason
- notes
- created_at
```

## 29.15 Supplier Return Items

```text
supplier_return_items
- id
- return_id
- item_id
- item_name
- qty
- purchase_price
- return_rate
- line_total
- imei_numbers
```

## 29.16 Repair Services

```text
repair_services
- id
- name
- description
- standard_charge
- taxable
- gst_rate
- sac_code
- estimated_duration_minutes
- warranty_days
- active
- created_at
- updated_at
```

## 29.17 Repair Jobs

```text
repair_jobs
- id
- job_number
- customer_id
- customer data
- device data
- intake information
- diagnosis
- technician notes
- customer notes
- internal notes
- estimated cost
- approved cost
- parts cost
- labor
- discount
- tax
- final total
- advance paid
- balance due
- status
- technician
- received time
- expected completion
- completed time
- delivered time
- warranty end
- approval state
- approval timestamp
- cancellation reason
- invoice number
- sale id
- created/updated timestamps
```

## 29.18 Repair Job Parts

```text
repair_job_parts
- id
- repair_job_id
- item_id
- part_name
- quantity
- unit_cost
- charge_amount
- status
- created_at
```

## 29.19 Repair Job Status History

```text
repair_job_status_history
- id
- repair_job_id
- from_status
- to_status
- notes
- changed_by
- changed_at
```

## 29.20 Schema migrations

Maintain a migration ledger so upgrades can be applied without destroying existing business data.

---

# 30. Transactional Integrity

Operations that modify multiple related records must be atomic.

Examples:

## 30.1 Sale

A checkout may update:

1. Sales header.
2. Sale lines.
3. Inventory quantity.
4. IMEI state.
5. Customer record.
6. Payment record.
7. Related repair data where applicable.

If one required operation fails, the entire transaction should roll back.

## 30.2 Supplier return

A supplier return may update:

1. Return header.
2. Return lines.
3. Inventory.
4. IMEI states.
5. Supplier payable/credit.

These must be atomic.

## 30.3 Repair parts

Consuming a part should atomically:

- Create consumption record.
- Decrease stock.
- Prevent negative stock unless explicitly permitted.

---

# 31. Concurrency Requirements

Standalone desktop mode has low concurrency, but a future multi-device implementation must assume concurrent checkouts.

The authoritative sale operation must be serialized/transactional.

Example:

```text
BEGIN IMMEDIATE

check stock

check IMEI availability

create sale

create sale items

decrement inventory

mark IMEI sold

COMMIT
```

Two terminals attempting to sell the last unit must not both succeed.

The second transaction must receive a clear out-of-stock/already-sold response.

---

# 32. Security Requirements

## 32.1 Master password

The application must support master-password protection.

Passwords must never be stored as plaintext.

## 32.2 Machine locking

The existing product uses machine fingerprinting and HMAC-SHA256 licensing.

A replacement may use a different licensing architecture, but the behavioral requirement remains:

- A license can be tied to a machine.
- Invalid/missing license state blocks normal use as configured.
- License validation should not expose secrets in renderer code.

## 32.3 Database protection

The database should not be casually exposed through arbitrary renderer access.

The preferred architecture is:

```text
UI
 ↓
controlled API / IPC
 ↓
database layer
```

## 32.4 Input validation

All user-supplied data must be validated.

SQL must use parameterized queries.

---

# 33. Application Architecture Requirements

The current application is an Electron desktop application with a local SQLite database.

A replacement implementation may use any equivalent architecture.

The important separation is:

```text
Presentation
    ↓
Application / Use Cases
    ↓
Domain Rules
    ↓
Persistence
```

The following concerns should not be tightly coupled:

- UI rendering.
- Tax calculation.
- Inventory rules.
- Sales transactions.
- Database queries.
- Printing.
- Backup.
- Licensing.

Pure business rules such as tax calculation, EMI calculation, inventory valuation, and filtering should be independently testable.

---

# 34. Recommended Domain Services

A clean replacement should have services approximately equivalent to:

```text
SalesService
InventoryService
PurchaseService
CustomerService
PaymentService
SupplierService
SupplierReturnService
ExpenseService
LiabilityService
RepairService
ReportingService
BackupService
ImportService
ExportService
TaxService
LicensingService
```

This is a recommendation, not a mandatory class naming scheme.

---

# 35. UI/UX Requirements

The exact visual design is **not part of the functional contract**.

A replacement can be:

- Modern.
- Minimal.
- Mobile-first.
- Traditional desktop POS.
- Dark.
- Light.
- Tablet optimized.

However, it must prioritize:

1. Fast billing.
2. Low typing effort.
3. Clear stock availability.
4. Clear payment state.
5. Clear customer debt.
6. Clear financial totals.
7. Safe destructive actions.
8. Strong audit visibility.

## 35.1 Required interaction patterns

Use:

- Search.
- Autocomplete.
- Tables.
- Filters.
- Sort.
- Pagination.
- Confirmation dialogs for destructive actions.
- Toast/status feedback.
- Empty states.
- Loading states.
- Validation errors.

---

# 36. Settings Requirements

## 36.1 Shop information

Support:

- Shop name.
- Address.
- Email.
- Phone.
- GSTIN.

## 36.2 Tax configuration

Support:

- Default GST rate.

## 36.3 Bank configuration

Support:

- Bank name.
- Account name.
- Account number.
- IFSC.
- Branch.

## 36.4 Finance providers

Support:

- List of known providers.
- Add provider.
- Remove provider.
- Normalization.
- Historical sales preservation.

## 36.5 Theme

The current application supports multiple visual themes, including:

- Dark.
- Light.
- Cyberpunk.
- Nord.
- Midnight.

A replacement is free to use different themes.

## 36.6 Repair feature flag

The repair module can be enabled/disabled.

Disabling the feature should hide or disable repair-specific workflows without deleting repair history.

---

# 37. Data Formatting

The product is intended for India.

Use:

- Indian currency formatting.
- INR / ₹.
- `en-IN` number formatting.
- Date/time formatting appropriate for Indian users.

Money should normally display to two decimal places in detailed financial contexts.

---

# 38. Printing Requirements

Printing is a first-class workflow.

The system should support printing:

- Sales invoices.
- Estimates.
- Sales reports.
- Inventory reports.
- Supplier return notes.
- Repair tickets.
- Repair invoices.

The implementation should allow printer/page format adaptation.

A replacement should not hard-code business logic into HTML print templates.

---

# 39. Import Requirements

## 39.1 Excel import

The application has support for structured Excel-based inventory/store import workflows.

The import pipeline should be capable of:

1. Detecting supported workbook structures.
2. Extracting structured data.
3. Validating records.
4. Mapping products/categories.
5. Importing records transactionally.
6. Reporting errors.
7. Avoiding duplicate creation where an identity key already exists.

## 39.2 Full-store import

Where a full-store workbook format is supported, import should handle all relevant datasets defined by that format rather than only one sheet.

## 39.3 JSON backup import

JSON backup import should be schema-aware and transactional.

---

# 40. Error Handling

The application must fail safely.

Examples:

### Invalid sale

Do not create partial sale records.

### Insufficient stock

Show a clear message and do not complete the transaction.

### Duplicate IMEI

Reject the transaction or require explicit correction.

### Invalid payment

Reject without changing the invoice.

### Invalid restore file

Do not modify the existing database.

### Printer failure

Do not roll back a completed sale merely because printing failed.

The sale should remain recorded and the user should be told that printing failed.

---

# 41. Auditability & Observability Requirements

The following events should remain reconstructable:

- Product stock-in.
- Product sale.
- Payment.
- Customer repayment.
- Sale void.
- Sale refund.
- Supplier return.
- Supplier payment.
- Liability repayment.
- Repair status change.
- Repair part consumption.
- Data import.
- Data restore.

Historical snapshots should be preferred over recomputing old values from mutable master records.

### 41.1 Request Correlation-ID & Diagnostic Traceability
Every client interaction and auditable operation must carry an end-to-end Correlation ID (`X-Correlation-ID`):
- Transmitted in HTTP request and response headers.
- Enriched onto every structured log entry emitted during request execution.
- Recorded in the audit log table (`audit_log.correlation_id`) to directly connect user-facing audit trails to server logs.
- Propagated to background worker jobs and distributed trace activities.
- Returned in standardized error responses so support teams can instantly pinpoint failing transactions from a cashier's error report.

### 41.2 Extended Reconciliation Sweep
The system must run automated daily reconciliation sweeping all operational projections against their underlying append-only ledgers:
1. Branch physical stock vs. inventory movements sum.
2. Invoice `amount_paid` vs. recorded payment transactions.
3. Customer `total_purchases` vs. active tax invoice grand totals.
4. Customer credit balances vs. customer ledger debit/credit entries.
5. Supplier payables vs. supplier ledger credit/debit entries.
Any divergence must trigger an immediate diagnostic alert.

### 41.3 Record-Level Deduplication for Data Portability
Data import tools (e.g. migration from desktop backups) must enforce record-level deduplication on natural keys (invoice numbers, IMEIs, customer phone numbers, supplier bills). Successive imports of updated backups must skip existing records and import new records incrementally without whole-file collision failures.

---

# 42. Data Integrity Constraints

The implementation should enforce:

- Unique invoice numbers.
- Unique repair job numbers.
- Valid sale status values.
- Valid payment modes.
- Valid item categories.
- Valid repair statuses.
- Valid IMEI statuses.
- Valid supplier return settlement types.
- Foreign-key relationships.
- Positive/valid monetary values.
- Non-negative quantities unless negative stock is explicitly required.
- Valid dates.

---

# 43. Database Performance

The database should have indexes for frequent queries.

At minimum:

- Item category.
- Purchase item.
- Purchase date.
- Sales date.
- Sales status.
- Sale item → sale.
- Sale item → item.
- Customer phone.
- Expense date.
- Customer payment → sale.
- Supplier transaction → supplier.
- Liability payment → liability.
- IMEI.
- IMEI status.
- Supplier return date.
- Supplier return → supplier.
- Repair job number.
- Repair job customer.
- Repair job status.
- Repair job received date.
- Repair job parts.
- Repair status history.

---

# 44. Multi-Device / Remote Architecture

This is an optional expansion of the standalone product, not a prerequisite for core offline operation.

The intended architecture is:

```text
Primary PC
  ├── Phone Zone UI
  ├── Embedded API server
  └── SQLite database

Secondary PC / mobile
  └── HTTP client
```

Optional remote access can be provided using a secure outbound tunnel.

## 44.1 Host

The primary machine:

- Hosts the database.
- Hosts the API.
- Remains the source of truth.

## 44.2 Pairing

Pairing may use a QR code containing:

- Server URL.
- LAN URL.
- Pairing token.

## 44.3 Authentication

Clients authenticate using bearer tokens or equivalent secure credentials.

## 44.4 RBAC

Suggested roles:

### Admin

Full access.

### Cashier/Floor Staff

Access:

- POS.
- Stock lookup.
- Customer lookup.

Restrict:

- Purchase cost.
- Profit.
- Expenses.
- Settings.

## 44.5 Mobile

A mobile client should support:

- POS.
- Inventory lookup.
- Barcode/IMEI scanning.
- Customer lookup.
- Optional Bluetooth thermal printing.

---

# 45. Backup Strategy

Because the local machine is a critical source of truth:

- Automated backup should be supported.
- Manual backup must be available.
- Backups should be timestamped.
- Restore should be tested.
- Backups should ideally be copied outside the primary disk.

For a multi-device deployment, automated periodic backups are strongly recommended.

---

# 46. Non-Functional Requirements

## 46.1 Reliability

The application must not lose committed sales or stock updates due to UI crashes.

## 46.2 Performance

Normal POS operations should feel instantaneous on typical shop hardware.

Target:

- Product search: sub-second.
- Cart update: effectively immediate.
- Tax calculation: immediate.
- Checkout: typically under one second locally.
- Dashboard: should not visibly freeze the UI.

## 46.3 Availability

Standalone business operations must work without internet access.

## 46.4 Maintainability

Business rules should be isolated from UI code.

## 46.5 Extensibility

The design should allow future additions such as:

- More payment modes.
- More product categories.
- Multiple stores.
- Multiple users.
- Cloud sync.
- Advanced accounting.
- Purchase invoice ingestion.
- Supplier integrations.

---

# 47. Testing Requirements

A reimplementation should include automated tests for business-critical behavior.

## 47.1 Tax tests

Test:

- Standard GST.
- Zero GST.
- Custom GST.
- Margin scheme.
- Margin <= 0.
- Discounts.
- Multiple cart lines.
- Rounding.

## 47.2 EMI tests

Test:

- Zero interest.
- Positive interest.
- Invalid tenure.
- Zero principal.
- Down payment greater than total.

## 47.3 Inventory tests

Test:

- Add stock.
- Sell stock.
- Insufficient stock.
- Low stock.
- Out of stock.
- Supplier return.
- IMEI availability.
- Duplicate IMEI.
- IMEI sold.
- IMEI returned.

## 47.4 Sales tests

Test:

- Tax invoice.
- Estimate.
- Full payment.
- Partial payment.
- Credit.
- Finance.
- Gifts.
- Discounts.
- Void.
- Refund.

## 47.5 Customer tests

Test:

- New customer.
- Returning customer.
- Credit calculation.
- Subsequent payment.
- Payment audit.

## 47.6 Repair tests

Test:

- Service creation.
- Service editing.
- Service activation.
- Job creation.
- Status transitions.
- Approval.
- Parts consumption.
- Delivery.
- Cancellation.
- Warranty.
- Repair billing.

## 47.7 Report tests

Test that report totals reconcile with the underlying transactions.

---

# 48. Critical Business Invariants

These are the rules that must never be violated.

### Invariant 1 — No double-selling a physical unit

An IMEI cannot be sold twice.

### Invariant 2 — No sale above available stock

A physical SKU cannot be sold beyond available quantity.

### Invariant 3 — Repair services do not count as stock

Repair services never contribute to physical inventory valuation.

### Invariant 4 — Historical invoices survive master-data changes

Changing an item's current name, price, HSN, or supplier must not rewrite historical invoice snapshots.

### Invariant 5 — Payments are transactions

A subsequent payment is a transaction, not an overwrite.

### Invariant 6 — Voids/refunds preserve history

A void/refund must not delete the original invoice.

### Invariant 7 — Supplier returns affect stock

Returned stock must leave sellable inventory.

### Invariant 8 — Returned IMEI becomes unavailable for normal sale

An IMEI returned to a supplier cannot remain `In Stock`.

### Invariant 9 — Estimates are not finalized sales

Estimates must not inflate revenue/GST/customer debt.

### Invariant 10 — Financial calculations reconcile

The sum of report components must reconcile with the underlying transaction set.

### Invariant 11 — Failed transactions are atomic

A failed multi-record operation leaves the database in its previous consistent state.

### Invariant 12 — Backups do not silently corrupt data

Restore/import must validate and fail safely.

---

# 49. Key End-to-End Workflows

## Workflow A — Sell a new phone

```text
Search/scan phone
    ↓
Select product
    ↓
Validate stock
    ↓
Enter/select customer
    ↓
Enter IMEI
    ↓
Calculate GST
    ↓
Apply discount if required
    ↓
Select payment / finance
    ↓
Validate payment
    ↓
Create sale transaction
    ↓
Create sale line
    ↓
Decrease stock
    ↓
Mark IMEI sold
    ↓
Update customer
    ↓
Commit
    ↓
Print/display invoice
```

## Workflow B — Sell a used phone under margin scheme

```text
Select Used Phone
    ↓
Confirm purchase cost
    ↓
Enable margin scheme
    ↓
Calculate margin
    ↓
Calculate GST only on positive margin
    ↓
Record sale
    ↓
Reduce stock
    ↓
Mark IMEI sold if tracked
```

## Workflow C — Customer buys on credit

```text
Create sale
    ↓
Grand total = ₹X
Amount paid = ₹Y
Outstanding = X-Y
    ↓
Save sale
    ↓
Customer ledger shows outstanding balance
    ↓
Later: Record Payment
    ↓
Create customer_payments transaction
    ↓
Recalculate balance
```

## Workflow D — Supplier return

```text
Select supplier
    ↓
Select stock
    ↓
Select IMEI(s) if applicable
    ↓
Enter return quantity/rate
    ↓
Choose settlement
    ↓
Create return
    ↓
Reduce inventory
    ↓
Mark IMEI Returned to Supplier
    ↓
Create supplier credit/payable adjustment
```

## Workflow E — Repair

```text
Receive device
    ↓
Create REP job number
    ↓
Record customer/device/problem
    ↓
Diagnose
    ↓
Request approval if needed
    ↓
Wait for parts if needed
    ↓
Perform repair
    ↓
Consume parts
    ↓
Calculate final bill
    ↓
Ready for Pickup
    ↓
Collect payment
    ↓
Deliver
    ↓
Set warranty
    ↓
Close job
```

---

# 50. What Is NOT a Functional Requirement

The following may be changed by an independent implementation without violating the product contract:

- Electron.
- Vite.
- Vanilla JavaScript.
- SQLite.
- Specific CSS.
- Exact screen layout.
- Exact colors.
- Exact icons.
- Exact typography.
- Exact animations.
- Exact navigation placement.
- Exact component library.
- Exact database table names, provided behavior and data integrity remain equivalent.

A different technology stack is acceptable.

---

# 51. Recommended Replacement Architecture

For an independent implementation, a robust architecture would be:

```text
                ┌─────────────────────┐
                │     Presentation    │
                │ Desktop / Web / App │
                └──────────┬──────────┘
                           │
                ┌──────────▼──────────┐
                │ Application Layer   │
                │ Use Cases / Commands│
                └──────────┬──────────┘
                           │
                ┌──────────▼──────────┐
                │ Domain Layer        │
                │ Sales / Tax / Stock │
                │ Payments / Repairs  │
                └──────────┬──────────┘
                           │
          ┌────────────────┼────────────────┐
          │                │                │
┌─────────▼────────┐ ┌─────▼──────┐ ┌──────▼───────┐
│ Persistence      │ │ Printing   │ │ Import/Export │
│ SQLite / SQL     │ │ Documents  │ │ XLSX / JSON   │
└──────────────────┘ └────────────┘ └───────────────┘
```

The domain layer should own the critical business rules.

---

# 52. Reimplementation Acceptance Criteria

A replacement should be considered functionally equivalent only when all of the following are true.

## POS

- [ ] Products can be searched/scanned.
- [ ] Stock is validated.
- [ ] Cart supports quantities.
- [ ] Customers can be selected.
- [ ] GST is calculated correctly.
- [ ] Margin scheme works only for used phones.
- [ ] Discounts work.
- [ ] IMEI can be captured.
- [ ] Cash/UPI/Card/Credit work.
- [ ] Partial payments work.
- [ ] Financing/EMI works.
- [ ] Invoices and estimates are generated.
- [ ] Gifts are supported.

## Inventory

- [ ] Physical categories work.
- [ ] Stock-in works.
- [ ] Stock-out works.
- [ ] Purchase history exists.
- [ ] IMEI tracking works.
- [ ] Supplier information works.
- [ ] Low/out-of-stock filtering works.
- [ ] Inventory valuation works.
- [ ] Supplier returns work.

## Sales

- [ ] Search works.
- [ ] Date filters work.
- [ ] Status filters work.
- [ ] Payment filters work.
- [ ] Financing filters work.
- [ ] Category filters work.
- [ ] Amount filters work.
- [ ] Sorting works.
- [ ] Pagination works.
- [ ] Invoice detail works.
- [ ] Void/refund workflows preserve history.

## Customers

- [ ] Customer directory works.
- [ ] Search works.
- [ ] Purchase history works.
- [ ] Credit balance works.
- [ ] Subsequent payments work.
- [ ] Repair history can be shown when enabled.

## Expenses

- [ ] Expense creation works.
- [ ] Categories work.
- [ ] Date filtering works.
- [ ] Monthly totals work.

## Suppliers

- [ ] Supplier master works.
- [ ] Bills work.
- [ ] Payments work.
- [ ] Opening balances work.
- [ ] Returns work.
- [ ] Payables reconcile.

## Liabilities

- [ ] Liability creation works.
- [ ] Types work.
- [ ] Balance works.
- [ ] Repayments are separate records.
- [ ] History is preserved.

## Repairs

- [ ] Service catalogue works.
- [ ] Repair tickets work.
- [ ] Job numbers are unique.
- [ ] Customer/device information works.
- [ ] Status lifecycle works.
- [ ] Status history works.
- [ ] Parts consumption works.
- [ ] Approval works.
- [ ] Billing works.
- [ ] Warranty works.

## Reports

- [ ] Daily sales report works.
- [ ] Inventory snapshot works.
- [ ] Payment breakdown works.
- [ ] Category summary works.
- [ ] Itemized sales log works.
- [ ] GSTR-1 export works.
- [ ] Inventory export works.

## Data

- [ ] Backup works.
- [ ] Restore works.
- [ ] Migrations work.
- [ ] Foreign keys/integrity work.
- [ ] Failed transactions roll back.

## Security

- [ ] Password protection works.
- [ ] Licensing works where applicable.
- [ ] Renderer/client cannot directly execute arbitrary database operations.
- [ ] SQL injection is prevented.

---

# 53. Important Implementation Notes

## 53.1 Do not copy the existing UI blindly

The UI is replaceable.

The following are the actual product differentiators:

- Fast billing.
- Correct tax behavior.
- Reliable stock tracking.
- IMEI traceability.
- Customer credit.
- Supplier/payables accounting.
- Repair lifecycle.
- Financial reporting.
- Auditability.
- Offline reliability.

## 53.2 Do not simplify the database into a few generic tables

A simplistic implementation such as:

```text
products
sales
customers
```

will not reproduce the business behavior.

The application requires separate ledgers for:

- Purchases.
- Sales.
- Payments.
- Supplier transactions.
- Supplier returns.
- IMEIs.
- Liabilities.
- Liability repayments.
- Repair jobs.
- Repair parts.
- Repair status history.

## 53.3 Do not calculate historical reports from mutable master data when snapshots exist

Historical financial documents should use values captured at the time of sale.

## 53.4 Do not use AI for core business logic

The current product does not require AI.

Deterministic rules are preferable for:

- Tax.
- Stock.
- IMEI.
- Payments.
- Reports.
- Imports.
- Supplier matching.

---

# 54. Product Definition in One Sentence

> **Phone Zone is an offline-first retail operating system for a mobile-phone shop: it connects inventory, IMEI traceability, billing, GST, customer credit, supplier payables, repairs, expenses, and business reporting into one auditable transactional system.**

---

# 55. Handover Interpretation

If this document is handed to another engineering team, they should be able to build a functionally equivalent product without seeing the original source code.

They are free to change:

- UI.
- Architecture implementation.
- Framework.
- Database technology.
- Component system.
- Branding.

They are **not** free to change the core domain behavior without treating it as a product change.

The authoritative contract is:

```text
Business entities
        +
Business workflows
        +
Financial rules
        +
Inventory rules
        +
IMEI rules
        +
Repair lifecycle
        +
Audit requirements
        +
Data integrity requirements
        =
Phone Zone functional behavior
```

---

## Appendix A — Current Technology Baseline

The current implementation uses:

- Electron desktop runtime.
- Vite frontend tooling.
- Vanilla JavaScript.
- Custom CSS/design system.
- SQLite.
- `better-sqlite3`.
- XLSX import/export.
- Local IPC between renderer and main process.
- Optional printing utilities.
- Feature flags for repair functionality.

These technologies are implementation details rather than contractual requirements.

---

## Appendix B — Current Application Data Model Summary

```text
settings
items
purchases
sales
sale_items
customers
customer_payments
expenses

suppliers
supplier_transactions

liabilities
liability_payments

inventory_imeis

supplier_returns
supplier_return_items

repair_services
repair_jobs
repair_job_parts
repair_job_status_history

schema_migrations
```

Relationships:

```text
Supplier
  ├── Purchases
  ├── Supplier Transactions
  └── Supplier Returns
          └── Return Items
                └── IMEIs

Item
  ├── Purchases
  ├── Sale Items
  ├── Inventory IMEIs
  ├── Repair Job Parts
  └── Supplier Return Items

Customer
  ├── Sales
  │     ├── Sale Items
  │     └── Customer Payments
  └── Repair Jobs

Sale
  ├── Sale Items
  └── Customer Payments

Repair Service
  └── Repair Jobs / POS Service Lines

Repair Job
  ├── Parts
  └── Status History

Liability
  └── Liability Payments
```

---

## Appendix C — Requirement Priority

### P0 — Must reproduce

- POS.
- Inventory.
- Sales.
- GST.
- Margin scheme.
- IMEI tracking.
- Customer credit.
- Payments.
- Supplier transactions.
- Supplier returns.
- Reports.
- Backup/restore.
- Transactional integrity.

### P1 — Core business modules

- Expenses.
- Liabilities.
- Repairs.
- Printing.
- Excel exports.
- GSTR-1 export.
- Finance/EMI.

### P2 — Operational enhancements

- Advanced import.
- Multi-device.
- Remote access.
- Role-based access.
- Automated backups.
- Mobile client.

---

## Appendix D — Source Baseline

This specification was derived from the Phone Zone repository's implemented application structure, database schema, reporting logic, POS/tax behavior, inventory/IMEI behavior, customer ledger, expense tracker, repair module, supplier/return model, settings, exports, and documented multi-device architecture.

The visual implementation is deliberately not reproduced as a requirement because the purpose of this document is to define the **behavioral and backend contract**, not to prescribe a pixel-identical clone.
