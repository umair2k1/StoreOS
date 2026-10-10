import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DailyAuditComponent } from './daily-audit.component';
import type { DailySalesReportData } from './daily-audit.component';
import { ReportsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('DailyAuditComponent', () => {
  let fixture: ComponentFixture<DailyAuditComponent>;
  let component: DailyAuditComponent;
  let reportsClient: ReportsClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockReport: DailySalesReportData = {
    date: '2026-10-08',
    sales: {
      revenue: 85000,
      buyingCost: 55000,
      profit: 30000,
      gstCollected: 12500,
      invoiceCount: 15,
      itemsSold: 22
    },
    inventory: {
      totalStockVolume: 450,
      stockWorthAtCost: 1200000,
      retailValuation: 1650000,
      lowStockCount: 4,
      outOfStockCount: 1
    },
    payments: {
      cash: 25000,
      upi: 40000,
      finance: 20000,
      other: 0,
      total: 85000
    },
    byCategory: [
      { category: 'MobilePhone', revenue: 65000, itemsSold: 3 },
      { category: 'Accessory', revenue: 20000, itemsSold: 19 }
    ],
    itemizedLog: [
      {
        invoiceNumber: 'INV-2026-0001',
        date: '2026-10-08',
        customerName: 'Rahul Verma',
        itemName: 'iPhone 13 128GB',
        qty: 1,
        sellingValue: 55000,
        costValue: 45000,
        gst: 8389.83,
        imei: '359123456789012',
        paymentMode: 'UPI'
      }
    ],
    collectedAmount: 85000,
    outstandingAmount: 0
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DailyAuditComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ReportsClient,
        {
          provide: ToastService,
          useValue: {
            success: vi.fn(),
            error: vi.fn(),
            info: vi.fn(),
            warning: vi.fn()
          }
        }
      ]
    }).compileComponents();

    reportsClient = TestBed.inject(ReportsClient);

    vi.spyOn(reportsClient, 'getDailySales').mockReturnValue(
      of(createFileResponse(mockReport))
    );

    fixture = TestBed.createComponent(DailyAuditComponent);
    component = fixture.componentInstance;
  });

  it('renders every section of the DailySalesReportDto', async () => {
    fixture.detectChanges();
    await component.loadReport();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;

    // 1. Sales Summary Section
    const salesSec = compiled.querySelector('.section-sales-summary');
    expect(salesSec).toBeTruthy();
    expect(salesSec?.textContent).toContain('85,000');
    expect(salesSec?.textContent).toContain('30,000'); // Gross Profit

    // 2. Financial Collections & Outstanding Section
    const finSec = compiled.querySelector('.section-financials');
    expect(finSec).toBeTruthy();
    expect(finSec?.textContent).toContain('85,000'); // Total Collected

    // 3. Payment Modes Breakdown Section
    const paySec = compiled.querySelector('.section-payments');
    expect(paySec).toBeTruthy();
    expect(paySec?.textContent).toContain('25,000'); // Cash
    expect(paySec?.textContent).toContain('40,000'); // UPI

    // 4. Inventory Valuation Section
    const invSec = compiled.querySelector('.section-inventory');
    expect(invSec).toBeTruthy();
    expect(invSec?.textContent).toContain('450 units');

    // 5. Category Sales Breakdown Section
    const catSec = compiled.querySelector('.section-category-sales');
    expect(catSec).toBeTruthy();
    expect(catSec?.textContent).toContain('MobilePhone');
    expect(catSec?.textContent).toContain('Accessory');

    // 6. Itemized Sale Log Section
    const itemSec = compiled.querySelector('.section-itemized-log');
    expect(itemSec).toBeTruthy();
    expect(itemSec?.textContent).toContain('INV-2026-0001');
    expect(itemSec?.textContent).toContain('iPhone 13 128GB');
    expect(itemSec?.textContent).toContain('359123456789012');

    // Print button exists
    const printBtn = compiled.querySelector('.btn-print');
    expect(printBtn).toBeTruthy();
  });

  it('renders shop branding and profit pill dynamically', async () => {
    fixture.detectChanges();
    await component.loadReport();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.shop-name')?.textContent).toContain('StoreOS');
    const profitPill = compiled.querySelector('.profit-pill');
    expect(profitPill).toBeTruthy();
    expect(profitPill?.textContent).toContain('10,000'); // 55,000 - 45,000 = +10,000
    expect(profitPill?.classList).toContain('profit-positive');
  });

  it('filters itemized log based on search query', async () => {
    fixture.detectChanges();
    await component.loadReport();
    fixture.detectChanges();

    component.searchQuery.set('NonExistentItem');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty-log-state')).toBeTruthy();

    component.searchQuery.set('iPhone');
    fixture.detectChanges();
    expect(compiled.querySelector('.item-title')?.textContent).toContain('iPhone 13 128GB');
  });
});
