import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SupplierInvoiceImportComponent } from './supplier-invoice-import.component';
import { ImportsClient, PurchasesClient, SuppliersClient } from '../../../core/api/generated-client';
import type { FileResponse, StockInCommand } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

describe('SupplierInvoiceImportComponent', () => {
  let fixture: ComponentFixture<SupplierInvoiceImportComponent>;
  let component: SupplierInvoiceImportComponent;
  let importsClient: ImportsClient;
  let purchasesClient: PurchasesClient;
  let suppliersClient: SuppliersClient;

  function createFileResponse(payload: unknown): FileResponse {
    return {
      data: new Blob([JSON.stringify(payload)], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    };
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SupplierInvoiceImportComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ImportsClient,
        PurchasesClient,
        SuppliersClient,
        ToastService
      ]
    }).compileComponents();

    importsClient = TestBed.inject(ImportsClient);
    purchasesClient = TestBed.inject(PurchasesClient);
    suppliersClient = TestBed.inject(SuppliersClient);

    vi.spyOn(suppliersClient, 'list').mockReturnValue(of(createFileResponse({ items: [] })));
    vi.spyOn(purchasesClient, 'stockIn').mockReturnValue(of(createFileResponse({ success: true })));

    fixture = TestBed.createComponent(SupplierInvoiceImportComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('(a) a response with zero ambiguous lines shows a success summary', async () => {
    const importPayload = {
      importBatchId: 'b-001',
      status: 'Imported',
      productsMatched: 1,
      productsAmbiguous: 0,
      ambiguousLines: [],
      purchasesCreated: 1
    };

    vi.spyOn(importsClient, 'importSupplierInvoice').mockReturnValue(of(createFileResponse(importPayload)));

    component.supplierId.set('sup-101');
    component.supplierInvoiceNo.set('INV-2026-001');
    component.rows.set([
      {
        id: 'row-1',
        productNameRaw: 'Samsung S24',
        hsnCode: '8517',
        qty: 1,
        rate: 75000,
        imei: '358912345678901'
      }
    ]);
    fixture.detectChanges();

    await component.submitImport();
    fixture.detectChanges();

    expect(component.successSummary()).toBeTruthy();
    expect(component.hasAmbiguousLines()).toBe(false);

    const nativeEl = fixture.nativeElement as HTMLElement;
    const summaryEl = nativeEl.querySelector('.success-summary');
    expect(summaryEl).toBeTruthy();
    expect(summaryEl?.textContent).toContain('Import completed successfully');
  });

  it('(b) a response with ambiguous lines renders the correct resolution UI per line type (no-match vs. multiple-match)', async () => {
    const importPayload = {
      importBatchId: 'b-002',
      status: 'Pending',
      productsMatched: 0,
      productsAmbiguous: 2,
      ambiguousLines: [
        {
          productNameRaw: 'Brand New Unknown Device',
          candidateProductIds: []
        },
        {
          productNameRaw: 'iPhone 15 Black',
          candidateProductIds: ['prod-apple-128', 'prod-apple-256']
        }
      ],
      purchasesCreated: 0
    };

    vi.spyOn(importsClient, 'importSupplierInvoice').mockReturnValue(of(createFileResponse(importPayload)));

    component.supplierId.set('sup-101');
    component.supplierInvoiceNo.set('INV-2026-002');
    component.rows.set([
      {
        id: 'row-1',
        productNameRaw: 'Brand New Unknown Device',
        hsnCode: '8517',
        qty: 2,
        rate: 20000,
        imei: ''
      },
      {
        id: 'row-2',
        productNameRaw: 'iPhone 15 Black',
        hsnCode: '8517',
        qty: 1,
        rate: 65000,
        imei: '351234567890123'
      }
    ]);
    fixture.detectChanges();

    await component.submitImport();
    fixture.detectChanges();

    expect(component.hasAmbiguousLines()).toBe(true);
    expect(component.ambiguousLines().length).toBe(2);

    const nativeEl = fixture.nativeElement as HTMLElement;

    // Line 1: No match
    const noMatchCard = nativeEl.querySelector('.no-match-line');
    expect(noMatchCard).toBeTruthy();
    const createShortcut = noMatchCard?.querySelector('.btn-create-product');
    expect(createShortcut).toBeTruthy();
    expect(createShortcut?.textContent).toContain('Create new product');

    // Line 2: Multiple matches
    const multiMatchCard = nativeEl.querySelector('.multiple-matches-line');
    expect(multiMatchCard).toBeTruthy();
    const candidateSelect = multiMatchCard?.querySelector('.candidate-select') as HTMLSelectElement | null;
    expect(candidateSelect).toBeTruthy();

    const options = multiMatchCard?.querySelectorAll('.candidate-select option');
    expect(options?.length).toBe(3);

    const retryBtn = multiMatchCard?.querySelector('.btn-retry') as HTMLButtonElement | null;
    expect(retryBtn).toBeTruthy();
    expect(retryBtn?.disabled).toBe(true);
  });

  it('(c) resolving a multiple-match line and clicking "Retry" calls the stock-in endpoint for just that line', async () => {
    const importPayload = {
      importBatchId: 'b-003',
      status: 'Pending',
      productsMatched: 0,
      productsAmbiguous: 1,
      ambiguousLines: [
        {
          productNameRaw: 'iPhone 15 Black',
          candidateProductIds: ['prod-apple-128', 'prod-apple-256']
        }
      ],
      purchasesCreated: 0
    };

    vi.spyOn(importsClient, 'importSupplierInvoice').mockReturnValue(of(createFileResponse(importPayload)));
    const stockInSpy = vi.spyOn(purchasesClient, 'stockIn').mockReturnValue(of(createFileResponse({ success: true })));

    component.supplierId.set('sup-101');
    component.supplierInvoiceNo.set('INV-2026-003');
    component.rows.set([
      {
        id: 'row-1',
        productNameRaw: 'iPhone 15 Black',
        hsnCode: '8517',
        qty: 1,
        rate: 65000,
        imei: '351234567890123'
      }
    ]);
    fixture.detectChanges();

    await component.submitImport();
    fixture.detectChanges();

    const nativeEl = fixture.nativeElement as HTMLElement;
    const selectEl = nativeEl.querySelector('.candidate-select') as HTMLSelectElement | null;
    expect(selectEl).toBeTruthy();

    if (selectEl) {
      selectEl.value = 'prod-apple-256';
      selectEl.dispatchEvent(new Event('change'));
    }

    fixture.detectChanges();
    await fixture.whenStable();

    const retryBtn = nativeEl.querySelector('.btn-retry') as HTMLButtonElement | null;
    expect(retryBtn?.disabled).toBe(false);

    retryBtn?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(stockInSpy).toHaveBeenCalledTimes(1);
    const calledCommand = stockInSpy.mock.calls[0][0] as StockInCommand;
    expect(calledCommand.productId).toBe('prod-apple-256');
    expect(calledCommand.qty).toBe(1);
    expect(calledCommand.purchaseRate).toBe(65000);
    expect(calledCommand.supplierInvoiceNo).toBe('INV-2026-003');
    expect(calledCommand.imeis).toEqual(['351234567890123']);

    fixture.detectChanges();
    const resolvedBadge = nativeEl.querySelector('.badge-resolved');
    expect(resolvedBadge).toBeTruthy();
  });
});
