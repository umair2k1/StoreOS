import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { StockInFormComponent } from './stock-in-form.component';
import { PurchasesClient } from '../../../core/api/generated-client';
import type { FileResponse } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

describe('StockInFormComponent', () => {
  let fixture: ComponentFixture<StockInFormComponent>;
  let component: StockInFormComponent;
  let purchasesClient: PurchasesClient;
  let toastService: ToastService;

  const mockFileResponse: FileResponse = {
    data: new Blob([JSON.stringify({ success: true })], { type: 'application/json' }),
    status: 200,
    fileName: 'stockin.json',
    headers: {}
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StockInFormComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        PurchasesClient,
        ToastService
      ]
    }).compileComponents();

    purchasesClient = TestBed.inject(PurchasesClient);
    toastService = TestBed.inject(ToastService);

    vi.spyOn(purchasesClient, 'stockIn').mockReturnValue(of(mockFileResponse));

    fixture = TestBed.createComponent(StockInFormComponent);
    component = fixture.componentInstance;
    component.productId = 'prod-123';
    component.productCategory = 'NewPhone';
    fixture.detectChanges();
  });

  it('(a) changing the quantity field adds/removes IMEI input rows to match', async () => {
    // Initial qty is 1 -> 1 IMEI field
    expect(component.isImeiTracked()).toBe(true);
    expect(component.imeis().length).toBe(1);

    let el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.imei-field-box').length).toBe(1);

    // Increase qty to 3
    component.onQtyChange(3);
    fixture.detectChanges();

    expect(component.imeis().length).toBe(3);
    el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.imei-field-box').length).toBe(3);

    // Decrease qty to 2
    component.onQtyChange(2);
    fixture.detectChanges();

    expect(component.imeis().length).toBe(2);
    el = fixture.nativeElement as HTMLElement;
    expect(el.querySelectorAll('.imei-field-box').length).toBe(2);
  });

  it('(b) entering a duplicate IMEI value across two rows highlights both as invalid before submission', async () => {
    component.onQtyChange(2);
    component.purchaseRate.set(50000);
    fixture.detectChanges();

    // Row 0 and Row 1 have distinct IMEIs
    component.updateImei(0, '354123456789012');
    component.updateImei(1, '354123456789099');
    fixture.detectChanges();

    expect(component.hasDuplicates()).toBe(false);
    expect(component.isDuplicate(0)).toBe(false);
    expect(component.isDuplicate(1)).toBe(false);

    // Introduce duplicate IMEI
    component.updateImei(1, '354123456789012');
    fixture.detectChanges();

    expect(component.hasDuplicates()).toBe(true);
    expect(component.isDuplicate(0)).toBe(true);
    expect(component.isDuplicate(1)).toBe(true);
    expect(component.isValid()).toBe(false);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.duplicate-alert-banner')).not.toBeNull();
    const invalidInputs = el.querySelectorAll('.imei-input-row.is-invalid');
    expect(invalidInputs.length).toBe(2);

    // Submit button must be disabled
    const submitBtn = el.querySelector('.btn-submit-stockin') as HTMLButtonElement;
    expect(submitBtn.disabled).toBe(true);
  });

  it('(c) submitting a valid form calls the endpoint with the correct payload shape and emits completion', async () => {
    const emitSpy = vi.spyOn(component.stockInCompleted, 'emit');
    const toastSpy = vi.spyOn(toastService, 'success');

    component.onQtyChange(2);
    component.purchaseRate.set(45000);
    component.supplierName.set('Apex Distribution');
    component.supplierInvoiceNo.set('INV-SUP-88');

    component.updateImei(0, '354123456789012');
    component.updateImei(1, '354123456789013');
    fixture.detectChanges();

    expect(component.isValid()).toBe(true);

    await component.submitStockIn();

    expect(purchasesClient.stockIn).toHaveBeenCalledWith(
      expect.objectContaining({
        productId: 'prod-123',
        qty: 2,
        purchaseRate: 45000,
        supplierName: 'Apex Distribution',
        supplierInvoiceNo: 'INV-SUP-88',
        imeis: ['354123456789012', '354123456789013']
      })
    );

    expect(toastSpy).toHaveBeenCalledWith(expect.stringContaining('Successfully stocked in 2 unit(s)'));
    expect(emitSpy).toHaveBeenCalled();
  });
});
