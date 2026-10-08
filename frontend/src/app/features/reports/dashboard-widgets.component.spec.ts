import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DashboardWidgetsComponent } from './dashboard-widgets.component';
import type { DashboardData } from './dashboard-widgets.component';
import { ReportsClient, SettingsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('DashboardWidgetsComponent', () => {
  let fixture: ComponentFixture<DashboardWidgetsComponent>;
  let component: DashboardWidgetsComponent;
  let reportsClient: ReportsClient;
  let settingsClient: SettingsClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockDashboard: DashboardData = {
    todayRevenue: 45000,
    todayInvoiceCount: 12,
    monthlyProfit: 125000,
    activeRepairsCount: 8,
    readyForPickupCount: 3,
    lowStockItems: [
      { productId: 'p1', name: 'USB-C Cable', stockQty: 2, threshold: 5 }
    ],
    topSellingItems: [
      { itemName: 'iPhone 13 Case', totalQtySold: 15 }
    ],
    sevenDayRevenue: [
      { date: '2026-10-02', revenue: 30000 },
      { date: '2026-10-03', revenue: 42000 }
    ]
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [DashboardWidgetsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ReportsClient,
        SettingsClient,
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
    settingsClient = TestBed.inject(SettingsClient);

    vi.spyOn(reportsClient, 'getDashboard').mockReturnValue(
      of(createFileResponse(mockDashboard))
    );
  });

  it('renders repair metrics when the repairs feature flag is enabled', async () => {
    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: true }))
    );

    fixture = TestBed.createComponent(DashboardWidgetsComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();
    await component.initData();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.isRepairsEnabled()).toBe(true);

    // Revenue metrics
    expect(compiled.querySelector('.revenue-card')?.textContent).toContain('45,000');
    expect(compiled.querySelector('.invoices-card')?.textContent).toContain('12');

    // Repair metrics MUST be visible
    expect(compiled.querySelector('.repairs-active-card')).toBeTruthy();
    expect(compiled.querySelector('.repairs-active-card')?.textContent).toContain('8');
    expect(compiled.querySelector('.repairs-ready-card')).toBeTruthy();
    expect(compiled.querySelector('.repairs-ready-card')?.textContent).toContain('3');
  });

  it('HARD CONSTRAINT: dashboard repair widgets are ENTIRELY hidden when the repairs feature is disabled', async () => {
    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: false }))
    );

    fixture = TestBed.createComponent(DashboardWidgetsComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();
    await component.initData();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.isRepairsEnabled()).toBe(false);

    // Core revenue widgets still render
    expect(compiled.querySelector('.revenue-card')?.textContent).toContain('45,000');
    expect(compiled.querySelector('.invoices-card')?.textContent).toContain('12');

    // Repair metrics MUST BE ENTIRELY HIDDEN (null in DOM, not just 0)
    expect(compiled.querySelector('.repairs-active-card')).toBeNull();
    expect(compiled.querySelector('.repairs-ready-card')).toBeNull();
  });
});
