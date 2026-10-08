import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { SupplierDetailComponent } from './supplier-detail.component';
import { SuppliersClient } from '../../../core/api/generated-client';
import type { FileResponse, RecordSupplierTransactionBody } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

describe('SupplierDetailComponent', () => {
  let fixture: ComponentFixture<SupplierDetailComponent>;
  let component: SupplierDetailComponent;
  let suppliersClient: SuppliersClient;

  function createFileResponse(payload: unknown): FileResponse {
    return {
      data: new Blob([JSON.stringify(payload)], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    };
  }

  const mockSupplier = {
    id: 'sup-101',
    name: 'Techno Imports Co',
    phone: '9876543210',
    address: 'Warehouse Hub 2',
    brandOrCategory: 'OnePlus / Smart Devices'
  };

  const initialPayable = {
    supplierId: 'sup-101',
    balance: 15000
  };

  const updatedPayable = {
    supplierId: 'sup-101',
    balance: 10000
  };

  const mockTransactions = [
    {
      id: 'txn-1',
      type: 'Bill',
      amount: 15000,
      paymentMode: 'Credit',
      referenceNo: 'BILL-001',
      txnDate: new Date('2026-02-01T10:00:00Z'),
      notes: 'Initial Stock'
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SupplierDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'id' ? 'sup-101' : null)
              }
            }
          }
        },
        SuppliersClient,
        ToastService
      ]
    }).compileComponents();

    suppliersClient = TestBed.inject(SuppliersClient);

    vi.spyOn(suppliersClient, 'getById').mockReturnValue(of(createFileResponse(mockSupplier)));
    vi.spyOn(suppliersClient, 'getPayable').mockReturnValue(of(createFileResponse(initialPayable)));
    vi.spyOn(suppliersClient, 'getTransactions').mockReturnValue(of(createFileResponse(mockTransactions)));
    vi.spyOn(suppliersClient, 'recordTransaction').mockReturnValue(of(createFileResponse({ success: true })));

    fixture = TestBed.createComponent(SupplierDetailComponent);
    component = fixture.componentInstance;
  });

  it('transaction recording updates the displayed payable correctly from backend query', async () => {
    // 1. Initial load
    fixture.detectChanges();
    await component.loadAllData('sup-101');
    fixture.detectChanges();

    expect(component.payableBalance()).toBe(15000);
    const nativeEl = fixture.nativeElement as HTMLElement;
    expect(nativeEl.querySelector('.payable-value')?.textContent).toContain('15,000');

    // 2. Open record modal and set values for a Payment of 5000
    component.openRecordModal();
    fixture.detectChanges();
    expect(component.isRecordModalOpen()).toBe(true);

    component.txnType.set('Payment');
    component.txnAmount.set(5000);
    component.txnPaymentMode.set('BankTransfer');
    component.txnReferenceNo.set('UTR-991283');
    component.txnDate.set('2026-02-15');
    component.txnNotes.set('Partial settlement');
    fixture.detectChanges();

    // 3. Mock updated backend response for getPayable (15000 - 5000 = 10000)
    const recordSpy = vi.spyOn(suppliersClient, 'recordTransaction').mockReturnValue(of(createFileResponse({ success: true })));
    vi.spyOn(suppliersClient, 'getPayable').mockReturnValue(of(createFileResponse(updatedPayable)));

    // 4. Submit transaction
    await component.submitRecordTransaction();
    fixture.detectChanges();

    expect(recordSpy).toHaveBeenCalledTimes(1);
    const sentBody = recordSpy.mock.calls[0][1] as RecordSupplierTransactionBody;
    expect(sentBody.type).toBe('Payment');
    expect(sentBody.amount).toBe(5000);
    expect(sentBody.paymentMode).toBe('BankTransfer');
    expect(sentBody.referenceNo).toBe('UTR-991283');

    // 5. Hard constraint verified: displayed payable is LIVE-updated from server's GetSupplierPayableQuery
    expect(component.payableBalance()).toBe(10000);
    expect(nativeEl.querySelector('.payable-value')?.textContent).toContain('10,000');
    expect(component.isRecordModalOpen()).toBe(false);
  });
});
