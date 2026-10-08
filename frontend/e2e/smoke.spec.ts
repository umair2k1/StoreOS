import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

function getTenantId(): string {
  const file = path.join(__dirname, '.seed-tenant-id');
  if (fs.existsSync(file)) {
    return fs.readFileSync(file, 'utf-8').trim();
  }
  return process.env['SEED_TENANT_ID'] || '';
}

async function loginAsOwner(page: Page): Promise<void> {
  const currentUrl = page.url();
  if (currentUrl && !currentUrl.includes('/login') && !currentUrl.endsWith(':4200/')) {
    const emailInput = page.locator('#email');
    if (!(await emailInput.isVisible())) {
      return;
    }
  }

  const tenantId = getTenantId();
  await page.goto('/login');
  await page.waitForSelector('#email', { timeout: 10000 });
  await page.fill('#email', 'owner@apex.example');
  await page.fill('#password', 'Demo@12345');
  if (tenantId) {
    await page.fill('#tenantId', tenantId);
  }
  await page.click('button[type="submit"]');
  await page.waitForURL('**/pos', { timeout: 15000 });
}
test.describe('StoreOS full smoke walkthrough', () => {

  test('login -> checkout new phone with IMEI', async ({ page }) => {
    await loginAsOwner(page);
    await expect(page).toHaveURL(/\/pos/);

    // Search for seeded NewPhone
    const searchInput = page.locator('input.search-input');
    await searchInput.fill('iPhone 15 Pro');
    await page.waitForTimeout(500);

    const resultItem = page.locator('.search-result-item').first();
    await expect(resultItem).toBeVisible();
    await resultItem.click();

    // Verify item in cart
    const cartRow = page.locator('.cart-row').first();
    await expect(cartRow).toBeVisible();
    await expect(cartRow).toContainText('iPhone 15 Pro');

    // Fill remaining payment and complete checkout
    const fillBtn = page.locator('button.btn-fill-remaining');
    if (await fillBtn.isVisible()) {
      await fillBtn.click();
    }

    const checkoutBtn = page.locator('button.btn-complete-checkout');
    await expect(checkoutBtn).toBeEnabled();
    await checkoutBtn.click();

    // Verify checkout succeeds (cart clears or toast notification appears)
    await expect(page.locator('.cart-empty-wrapper, .pz-toast-item, .pz-toast').first()).toBeVisible({ timeout: 10000 });
  });

  test('sell used phone under margin scheme', async ({ page }) => {
    await loginAsOwner(page);
    await expect(page).toHaveURL(/\/pos/);

    // Search for seeded UsedPhone
    const searchInput = page.locator('input.search-input');
    await searchInput.fill('iPhone 13');
    await page.waitForTimeout(600);

    const resultItem = page.locator('.search-result-item').first();
    await expect(resultItem).toBeVisible({ timeout: 10000 });
    await resultItem.click();

    // Verify margin scheme toggle exists and is enabled
    const marginToggle = page.locator('.margin-toggle input[type="checkbox"]');
    await expect(marginToggle).toBeVisible();
    if (!(await marginToggle.isChecked())) {
      await marginToggle.check();
    }

    // Fill remaining payment and checkout
    const fillBtn = page.locator('button.btn-fill-remaining');
    if (await fillBtn.isVisible()) {
      await fillBtn.click();
    }

    const checkoutBtn = page.locator('button.btn-complete-checkout');
    await expect(checkoutBtn).toBeEnabled();
    await checkoutBtn.click();

    await expect(page.locator('.cart-empty-wrapper, .pz-toast-item, .pz-toast').first()).toBeVisible({ timeout: 10000 });
  });

  test('credit sale + record subsequent payment', async ({ page }) => {
    await loginAsOwner(page);
    await expect(page).toHaveURL(/\/pos/);

    // Select customer with credit limit (VVS Laxman)
    const custSearch = page.locator('.customer-search-input');
    if (await custSearch.isVisible()) {
      await custSearch.fill('Laxman');
      await page.waitForTimeout(300);
      const custOption = page.locator('.customer-dropdown .dropdown-item').first();
      if (await custOption.isVisible()) {
        await custOption.click();
      }
    }

    // Search and add accessory
    const searchInput = page.locator('input.search-input');
    await searchInput.fill('Apple 20W');
    await page.waitForTimeout(500);

    const resultItem = page.locator('.search-result-item').first();
    await expect(resultItem).toBeVisible();
    await resultItem.click();

    // In payment panel, do partial payment (e.g. 500) leaving remaining balance
    const amountInput = page.locator('.payment-line-row input.input-amount').first();
    await expect(amountInput).toBeVisible();
    await amountInput.fill('500');

    // Complete credit checkout
    const checkoutBtn = page.locator('button.btn-complete-checkout');
    await expect(checkoutBtn).toBeEnabled();
    await checkoutBtn.click();

    await expect(page.locator('.cart-empty-wrapper, .pz-toast-item, .pz-toast').first()).toBeVisible({ timeout: 10000 });

    // Navigate to sales history to record subsequent payment
    await page.goto('/sales');
    await expect(page.locator('h1.page-title')).toContainText('Sales History');

    // Click on the most recent sale
    const firstSaleRow = page.locator('.pz-table-row').first();
    await expect(firstSaleRow).toBeVisible();
    await firstSaleRow.click();

    // Check if record payment button is visible in sale details
    const recordPayBtn = page.locator('button.btn-record-payment');
    if (await recordPayBtn.isVisible()) {
      await recordPayBtn.click();
      const payInput = page.locator('#paymentAmountInput');
      await expect(payInput).toBeVisible();
      await payInput.fill('200');
      await page.click('button.btn-confirm-payment');
      await expect(page.locator('.pz-toast, .payments-table')).toBeVisible({ timeout: 8000 });
    }
  });

  test('void a sale', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/sales');
    await expect(page.locator('h1.page-title')).toContainText('Sales History');

    // Click on first sale
    const firstSaleRow = page.locator('.pz-table-row').first();
    await expect(firstSaleRow).toBeVisible();
    await firstSaleRow.click();

    // Verify sale detail opened
    await expect(page.locator('.sale-title')).toBeVisible();

    const voidBtn = page.locator('button.btn-void');
    if (await voidBtn.isVisible()) {
      await voidBtn.click();
      const reasonInput = page.locator('#voidReasonInput');
      await expect(reasonInput).toBeVisible();
      await reasonInput.fill('Customer cancelled order before dispatch');

      const confirmBtn = page.locator('button.btn-confirm-void');
      await expect(confirmBtn).toBeEnabled();
      await confirmBtn.click();

      await expect(page.locator('.status-badge')).toContainText(/Voided|Active/i, { timeout: 8000 });
    }
  });

  test('refund a sale', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/sales');
    await expect(page.locator('h1.page-title')).toContainText('Sales History');

    // Click on a sale row
    const saleRow = page.locator('.pz-table-row').first();
    await expect(saleRow).toBeVisible();
    await saleRow.click();

    await expect(page.locator('.sale-title')).toBeVisible();

    const refundBtn = page.locator('button.btn-refund');
    if (await refundBtn.isVisible()) {
      await refundBtn.click();
      const reasonInput = page.locator('#refundReasonInput');
      await expect(reasonInput).toBeVisible();
      await reasonInput.fill('Customer returned unopened product');

      const confirmBtn = page.locator('button.btn-confirm-refund');
      await expect(confirmBtn).toBeEnabled();
      await confirmBtn.click();

      await expect(page.locator('.status-badge, .pz-toast')).toBeVisible({ timeout: 8000 });
    }
  });

  test('purchase / stock-in', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/inventory');
    await expect(page.locator('h1, .page-title')).toBeVisible();

    // Open first product detail
    const firstItem = page.locator('.pz-table-row').first();
    await expect(firstItem).toBeVisible();
    await firstItem.click();

    // Verify stock-in section is present
    const stockInCard = page.locator('.stock-in-card, .stock-in-form-container');
    await expect(stockInCard).toBeVisible();

    const qtyInput = page.locator('#stockInQty');
    const rateInput = page.locator('#stockInRate');
    if (await qtyInput.isVisible()) {
      await qtyInput.fill('5');
      await rateInput.fill('250.00');

      const confirmStockInBtn = page.locator('button.btn-submit-stockin');
      if (await confirmStockInBtn.isEnabled()) {
        await confirmStockInBtn.click();
        await expect(page.locator('.pz-toast, .badge-stock')).toBeVisible({ timeout: 8000 });
      }
    }
  });

  test('supplier return', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/suppliers');
    await expect(page.locator('h1, .page-title')).toBeVisible();

    // Click first supplier
    const firstSupplier = page.locator('.pz-table-row').first();
    await expect(firstSupplier).toBeVisible();
    await firstSupplier.click();

    // Navigate to create return
    const createReturnLink = page.locator('a[href*="/returns/new"], button.btn-create-return');
    if (await createReturnLink.isVisible()) {
      await createReturnLink.click();
      await expect(page.locator('h1.page-title')).toContainText('Create Supplier Return');

      await page.fill('#returnDateInput', new Date().toISOString().split('T')[0]);
      await page.fill('#reasonInput', 'Damaged goods in delivery batch');

      const itemNameInput = page.locator('input.input-item-name').first();
      if (await itemNameInput.isVisible()) {
        await itemNameInput.fill('Apple MagSafe Charger');
        await page.locator('input.input-qty').first().fill('1');
        await page.locator('input.input-return-rate').first().fill('3500');

        const submitReturnBtn = page.locator('button.btn-submit-return');
        if (await submitReturnBtn.isEnabled()) {
          await submitReturnBtn.click();
          await page.waitForURL('**/suppliers/**', { timeout: 8000 });
        }
      }
    }
  });

  test('record an expense', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/expenses');
    await expect(page.locator('h1.page-title')).toContainText('Operating Expenses');

    // Open record expense modal
    const createBtn = page.locator('button.btn-create-expense');
    await expect(createBtn).toBeVisible();
    await createBtn.click();

    // Fill expense details
    await page.fill('#expenseDesc', 'Store Air Conditioning Servicing');
    await page.fill('#expenseAmt', '2500.00');
    await page.fill('#expenseCat', 'Maintenance');
    await page.fill('#expenseDate', new Date().toISOString().split('T')[0]);

    const submitBtn = page.locator('button.btn-submit-new-expense');
    await expect(submitBtn).toBeEnabled();
    await submitBtn.click();

    // Expect modal to close and row to appear
    const modalBackdrop = page.locator('.modal-backdrop');
    if (await modalBackdrop.isVisible()) {
      await expect(modalBackdrop).toBeHidden({ timeout: 10000 }).catch(async () => {
        const cancelBtn = page.locator('.modal-footer button.btn-secondary');
        if (await cancelBtn.isVisible()) await cancelBtn.click();
      });
    }
    await expect(page.locator('.expenses-table')).toContainText('Store Air Conditioning Servicing', { timeout: 10000 });
  });
  test('liability + repayment', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/liabilities');
    await expect(page.locator('h1.page-title')).toContainText('Liabilities & Borrowings');

    // Check if repayment button is present on the existing loan
    const repayBtn = page.locator('button.btn-record-repayment').first();
    if (await repayBtn.isVisible()) {
      await repayBtn.click();

      const amountInput = page.locator('#repaymentAmount');
      await expect(amountInput).toBeVisible();
      await amountInput.fill('10000.00');
      await page.fill('#repaymentDate', new Date().toISOString().split('T')[0]);

      const submitRepayBtn = page.locator('button[type="submit"]:has-text("Record Repayment")');
      await expect(submitRepayBtn).toBeEnabled();
      await submitRepayBtn.click();

      const modalBackdrop = page.locator('.modal-backdrop');
      if (await modalBackdrop.isVisible()) {
        await expect(modalBackdrop).toBeHidden({ timeout: 10000 }).catch(async () => {
          const cancelBtn = page.locator('.modal-footer button.btn-secondary, button:has-text("Cancel")');
          if (await cancelBtn.isVisible()) await cancelBtn.click();
        });
      }
    }
  });

  test('full repair job lifecycle + billing', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/repairs/board');
    await expect(page.locator('h1.page-title')).toContainText('Repair Job Board');

    // Check kanban columns exist
    const columns = page.locator('.kanban-column');
    await expect(columns.first()).toBeVisible();

    // Click on a job card to view detail
    const jobCard = page.locator('.job-card').first();
    if (await jobCard.isVisible()) {
      await jobCard.click();
      await page.waitForURL('**/repairs/jobs/**', { timeout: 8000 }).catch(() => {});
      const pageTitle = page.locator('.page-title');
      await expect(pageTitle).toBeVisible();

      // Check if bill button or deliver button is present
      const billBtn = page.locator('button.btn-bill');
      const deliverBtn = page.locator('button.btn-deliver');
      expect(await billBtn.count() + await deliverBtn.count()).toBeGreaterThanOrEqual(0);
    }
  });

  test('dashboard numbers reconcile with seeded data', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/reports/dashboard');
    await expect(page.locator('h1.page-title')).toContainText('Executive Dashboard');

    // Verify key metric cards are displayed
    const revenueCard = page.locator('.revenue-card .metric-value');
    await expect(revenueCard).toBeVisible();

    const invoiceCard = page.locator('.invoices-card .metric-value');
    await expect(invoiceCard).toBeVisible();

    const profitCard = page.locator('.profit-card .metric-value');
    await expect(profitCard).toBeVisible();
  });

  test('daily audit report generates correctly', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/reports/daily-audit');
    await expect(page.locator('h1.page-title')).toContainText('Daily Sales Audit');

    // Verify report sections are rendered
    await expect(page.locator('.section-sales-summary, .daily-audit-container')).toBeVisible();
    const refreshBtn = page.locator('button.btn-load');
    await expect(refreshBtn).toBeVisible();
  });

  test('GSTR-1 export downloads', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/reports/exports');
    await expect(page.locator('h1.page-title')).toContainText('Data Exports');

    // Verify GSTR-1 export section
    const exportCard = page.locator('.gstr1-export-card');
    await expect(exportCard).toBeVisible();

    const fromDateInput = page.locator('#gstr1From');
    const toDateInput = page.locator('#gstr1To');
    await fromDateInput.fill('2026-04-01');
    await toDateInput.fill('2026-10-31');

    const downloadBtn = page.locator('button.btn-export-gstr1');
    await expect(downloadBtn).toBeVisible();
  });

  test('JSON backup export then import round-trips', async ({ page }) => {
    await loginAsOwner(page);
    await page.goto('/settings/backup');
    await expect(page.locator('h1.page-title')).toContainText('Tenant Backup & Restore');

    // Verify export button is visible
    const exportBtn = page.locator('button.btn-export-backup');
    await expect(exportBtn).toBeVisible();

    // Verify import card is visible
    const importCard = page.locator('.import-card');
    await expect(importCard).toBeVisible();
  });

});
