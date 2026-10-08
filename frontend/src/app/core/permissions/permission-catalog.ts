/**
 * StoreOS Permission Catalog (Frontend Mirror)
 *
 * NOTE: This list mirrors `StoreOS.Domain.Identity.PermissionCatalog` (P-115) exactly.
 * The two lists MUST stay synchronized. Any added, renamed, or deleted permission code
 * on the backend must be reflected here immediately.
 */

export const PERMISSION_CODES = [
  'pos:checkout',
  'sales:discount:apply',
  'sales:void',
  'sales:refund',
  'sales:credit:override',
  'sales:read:any-branch',
  'inventory:cost:view',
  'reports:profit:view',
  'inventory:manage',
  'purchases:manage',
  'inventory:adjustments',
  'expenses:manage',
  'liabilities:manage',
  'repairs:manage',
  'settings:manage',
  'users:manage',
  'branches:manage'
] as const;

export type PermissionCode = (typeof PERMISSION_CODES)[number];

export interface PermissionDefinition {
  code: PermissionCode;
  label: string;
  category: 'POS & Sales' | 'Inventory & Catalog' | 'Finance & Expenses' | 'Administration';
  description: string;
}

export const PERMISSION_DEFINITIONS: readonly PermissionDefinition[] = [
  {
    code: 'pos:checkout',
    label: 'POS Checkout',
    category: 'POS & Sales',
    description: 'Process and complete counter point-of-sale sales and issue invoices.'
  },
  {
    code: 'sales:discount:apply',
    label: 'Apply Sales Discounts',
    category: 'POS & Sales',
    description: 'Apply line-item or bill-level discounts at checkout.'
  },
  {
    code: 'sales:void',
    label: 'Void Sales',
    category: 'POS & Sales',
    description: 'Void completed sales transactions within the allowed void window.'
  },
  {
    code: 'sales:refund',
    label: 'Process Refunds',
    category: 'POS & Sales',
    description: 'Issue partial or full payment refunds on past sales invoices.'
  },
  {
    code: 'sales:credit:override',
    label: 'Credit Limit Override',
    category: 'POS & Sales',
    description: 'Allow store credit sales exceeding a customer established credit limit.'
  },
  {
    code: 'sales:read:any-branch',
    label: 'Cross-Branch Sales View',
    category: 'POS & Sales',
    description: 'View sales history and receipts from all branches across the tenant.'
  },
  {
    code: 'inventory:cost:view',
    label: 'View Inventory Costs',
    category: 'Inventory & Catalog',
    description: 'View wholesale purchase prices and margin percentages in catalog & stock screens.'
  },
  {
    code: 'reports:profit:view',
    label: 'View Profit Reports',
    category: 'Finance & Expenses',
    description: 'View gross margin, net profitability, and financial analytics on reports.'
  },
  {
    code: 'inventory:manage',
    label: 'Manage Products & Stock',
    category: 'Inventory & Catalog',
    description: 'Create, edit, and deactivate catalog products and adjust stock parameters.'
  },
  {
    code: 'purchases:manage',
    label: 'Manage Supplier Purchases',
    category: 'Inventory & Catalog',
    description: 'Record incoming supplier purchase bills and manage supplier ledger.'
  },
  {
    code: 'liabilities:manage',
    label: 'Manage Liabilities & Loans',
    category: 'Finance & Expenses',
    description: 'Track external debts, loans, promissory notes, and record repayments.'
  },
  {
    code: 'expenses:manage',
    label: 'Manage Operating Expenses',
    category: 'Finance & Expenses',
    description: 'Record, edit, and categorize day-to-day store operational expenses.'
  },
  {
    code: 'inventory:adjustments',
    label: 'Inventory Stock In/Out Adjustments',
    category: 'Inventory & Catalog',
    description: 'Perform ad-hoc stock count corrections, damaged stock write-offs, and inward receipts.'
  },
  {
    code: 'repairs:manage',
    label: 'Manage Service Center & Repairs',
    category: 'Administration',
    description: 'Manage repair intake, diagnosis bench, part consumption, and repair billing.'
  },
  {
    code: 'settings:manage',
    label: 'Manage Store Settings',
    category: 'Administration',
    description: 'Modify store profile, tax rules, payment methods, and tenant features.'
  },
  {
    code: 'users:manage',
    label: 'Manage Users & Assignments',
    category: 'Administration',
    description: 'Create and deactivate employees, reset credentials, and assign branches.'
  },
  {
    code: 'branches:manage',
    label: 'Manage Store Branches',
    category: 'Administration',
    description: 'Create and update store branch locations, addresses, and GSTIN mappings.'
  }
];
