import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { EstimateDetailComponent } from './estimate-detail.component';
import { PosClient, SalesClient } from '../../../core/api/generated-client';
import type { SaleDetailDto, FileResponse } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import { OverlayModule } from '@angular/cdk/overlay';

describe('EstimateDetailComponent', () => {
  let fixture: ComponentFixture<EstimateDetailComponent>;
  let component: EstimateDetailComponent;
  let posClient: PosClient;
  let salesClient: SalesClient;
  let toastService: ToastService;
  let router: Router;

  const mockEstimate: SaleDetailDto = {
    id: 'est-12345',
    invoiceNumber: 'EST-2026-0001',
    invoiceType: 'estimate',
    saleDate: new Date('2026-10-08T10:00:00Z'),
    customerId: 'cust-999',
    customerName: 'Aarav Sharma',
    totalTaxable: 10000,
    totalCgst: 900,
    totalSgst: 900,
    grandTotal: 11800,
    discount: 0,
    items: [
      {
        id: 'line-1',
        itemName: 'Samsung Galaxy S24',
        category: 'Phone',
        qty: 1,
        pricePerUnit: 11800,
        taxable: 10000,
        cgst: 900,
        sgst: 900
      }
    ]
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [EstimateDetailComponent, OverlayModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        PosClient,
        SalesClient,
        ToastService,
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'id' ? 'est-12345' : null)
              }
            }
          }
        }
      ]
    }).compileComponents();

    posClient = TestBed.inject(PosClient);
    salesClient = TestBed.inject(SalesClient);
    toastService = TestBed.inject(ToastService);
    router = TestBed.inject(Router);

    vi.spyOn(salesClient, 'getSaleById').mockReturnValue(of(mockEstimate));
    vi.spyOn(router, 'navigate').mockResolvedValue(true);

    fixture = TestBed.createComponent(EstimateDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads and displays estimate details on init without rebuilding cart', () => {
    expect(salesClient.getSaleById).toHaveBeenCalledWith('est-12345');
    expect(component.estimate()).toEqual(mockEstimate);
    expect(component.isConverted()).toBe(false);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('EST-2026-0001');
    expect(el.textContent).toContain('Aarav Sharma');
    expect(el.textContent).toContain('Samsung Galaxy S24');
  });

  it('prompts for payment details and calls convertEstimate without re-entering line items', async () => {
    // 1. Click Convert to Invoice to show payment prompt
    component.startConversion();
    fixture.detectChanges();

    expect(component.showPaymentPrompt()).toBe(true);

    // 2. Set payments
    component.onPaymentsChange([{ paymentMode: 'upi', amount: 11800 }]);

    // 3. Mock posClient.convertEstimate returning new tax invoice
    const convertPayload = JSON.stringify({
      saleId: 'sale-98765',
      invoiceNumber: 'INV-2026-0001',
      grandTotal: 11800,
      amountPaid: 11800,
      balance: 0
    });

    const mockResponse: FileResponse = {
      data: new Blob([convertPayload], { type: 'application/json' }),
      status: 201,
      fileName: 'invoice.json',
      headers: {}
    };

    const convertSpy = vi.spyOn(posClient, 'convertEstimate').mockReturnValue(of(mockResponse));
    const toastSpy = vi.spyOn(toastService, 'success');

    // 4. Trigger convert
    await component.convert();

    expect(convertSpy).toHaveBeenCalledWith(
      'est-12345',
      expect.objectContaining({
        payments: [{ paymentMode: 'upi', amount: 11800 }]
      })
    );

    // 5. Verifies toast and router navigation to the resulting sale
    expect(toastSpy).toHaveBeenCalledWith(expect.stringContaining('INV-2026-0001'));
    expect(router.navigate).toHaveBeenCalledWith(['/sales', 'sale-98765']);
  });

  it('displays converted badge and suppresses convert actions when estimate is already converted', () => {
    const convertedEstimate: SaleDetailDto = {
      ...mockEstimate,
      linkedTaxInvoiceId: 'sale-already-converted',
      status: 'converted'
    };

    component.estimate.set(convertedEstimate);
    fixture.detectChanges();

    expect(component.isConverted()).toBe(true);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('This estimate has been converted to an official Tax Invoice.');
    expect(el.querySelector('.btn-convert-prominent')).toBeNull();
  });
});
