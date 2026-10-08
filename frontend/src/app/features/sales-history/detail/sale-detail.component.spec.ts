import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute } from '@angular/router';
import { OverlayModule } from '@angular/cdk/overlay';
import { of } from 'rxjs';
import { SaleDetailComponent } from './sale-detail.component';
import { VoidDialogComponent } from './void-dialog.component';
import { RefundDialogComponent } from './refund-dialog.component';
import { SalesClient } from '../../../core/api/generated-client';
import type { SaleDetailDto, FileResponse } from '../../../core/api/generated-client';
import { AuthSignalStore } from '../../../core/auth/auth-signal-store';
import { ToastService } from '../../../shared/services/toast.service';
import { PzDialogService, PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';

describe('SaleDetailComponent', () => {
  let fixture: ComponentFixture<SaleDetailComponent>;
  let component: SaleDetailComponent;
  let salesClient: SalesClient;
  let authStore: AuthSignalStore;
  let dialogService: PzDialogService;

  const mockActiveSaleWithDue: SaleDetailDto = {
    id: 'sale-111',
    invoiceNumber: 'INV-2026-0111',
    invoiceType: 'tax_invoice',
    saleDate: new Date('2026-10-08T10:00:00Z'),
    customerName: 'Priya Patel',
    status: 'Active',
    grandTotal: 10000,
    amountPaid: 6000,
    balance: 4000,
    items: [
      {
        id: 'item-1',
        itemName: 'Redmi Note 13',
        qty: 1,
        pricePerUnit: 10000,
        taxable: 8474.58,
        cgst: 762.71,
        sgst: 762.71
      }
    ],
    payments: [
      {
        id: 'pay-1',
        amount: 6000,
        paymentMode: 'cash',
        paymentDate: new Date('2026-10-08T10:00:00Z')
      }
    ]
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SaleDetailComponent, OverlayModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        SalesClient,
        AuthSignalStore,
        ToastService,
        PzDialogService,
        { provide: PzDialogRef, useValue: { close: vi.fn() } },
        {
          provide: PZ_DIALOG_DATA,
          useValue: {
            saleId: 'sale-111',
            invoiceNumber: 'INV-2026-0111',
            balance: 4000,
            sale: mockActiveSaleWithDue
          }
        },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'id' ? 'sale-111' : null)
              }
            }
          }
        }
      ]
    }).compileComponents();

    salesClient = TestBed.inject(SalesClient);
    authStore = TestBed.inject(AuthSignalStore);
    dialogService = TestBed.inject(PzDialogService);

    // Default permissions: allow void and refund
    vi.spyOn(authStore, 'hasPermission').mockImplementation((perm) => {
      return perm === 'sales:void' || perm === 'sales:refund';
    });

    vi.spyOn(salesClient, 'getSaleById').mockReturnValue(of(mockActiveSaleWithDue));

    fixture = TestBed.createComponent(SaleDetailComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  describe('(a) action buttons visibility rules', () => {
    it('shows Record Payment when balance > 0 and status is Active', () => {
      const el = fixture.nativeElement as HTMLElement;
      expect(component.canRecordPayment()).toBe(true);
      expect(el.querySelector('.btn-record-payment')).not.toBeNull();
    });

    it('hides Record Payment when balance is 0', () => {
      component.sale.set({
        ...mockActiveSaleWithDue,
        amountPaid: 10000,
        balance: 0
      });
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      expect(component.canRecordPayment()).toBe(false);
      expect(el.querySelector('.btn-record-payment')).toBeNull();
    });

    it('shows Void and Refund when status is Active and user has permissions', () => {
      const el = fixture.nativeElement as HTMLElement;
      expect(component.isActive()).toBe(true);
      expect(el.querySelector('.btn-void')).not.toBeNull();
      expect(el.querySelector('.btn-refund')).not.toBeNull();
    });

    it('hides Void and Refund when status is not Active (e.g. Voided)', () => {
      component.sale.set({
        ...mockActiveSaleWithDue,
        status: 'Voided'
      });
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      expect(component.isActive()).toBe(false);
      expect(el.querySelector('.btn-void')).toBeNull();
      expect(el.querySelector('.btn-refund')).toBeNull();
      expect(el.querySelector('.btn-record-payment')).toBeNull();
    });

    it('hides Void button when user lacks sales:void permission', () => {
      vi.spyOn(authStore, 'hasPermission').mockImplementation((perm) => perm !== 'sales:void');
      // Re-trigger permission check by updating signal or re-evaluating
      fixture = TestBed.createComponent(SaleDetailComponent);
      component = fixture.componentInstance;
      fixture.detectChanges();

      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('.btn-void')).toBeNull();
    });
  });

  describe('(b) Void/Refund reason validation', () => {
    it('VoidDialog requires non-empty reason before submit button is enabled', async () => {
      const voidFixture = TestBed.createComponent(VoidDialogComponent);
      const voidComp = voidFixture.componentInstance;
      voidFixture.detectChanges();

      // Initially empty reason
      expect(voidComp.isValid()).toBe(false);

      // Whitespace only
      voidComp.reason.set('   ');
      expect(voidComp.isValid()).toBe(false);

      // Valid reason
      voidComp.reason.set('Customer requested cancellation before pickup');
      expect(voidComp.isValid()).toBe(true);
    });

    it('RefundDialog requires non-empty reason and positive amount before submit is enabled', async () => {
      const refundFixture = TestBed.createComponent(RefundDialogComponent);
      const refundComp = refundFixture.componentInstance;
      refundFixture.detectChanges();

      // Initially empty reason
      expect(refundComp.isValid()).toBe(false);

      // Reason with positive amount
      refundComp.reason.set('Defective product returned');
      refundComp.refundAmount.set(5000);
      expect(refundComp.isValid()).toBe(true);

      // Zero amount invalidates
      refundComp.refundAmount.set(0);
      expect(refundComp.isValid()).toBe(false);
    });
  });

  describe('(c) successful action refreshes the detail view', () => {
    it('voiding a sale calls voidSale endpoint and refreshes sale details', async () => {
      const voidResult: FileResponse = {
        data: new Blob([]),
        status: 200,
        fileName: 'void.txt',
        headers: {}
      };

      const voidSpy = vi.spyOn(salesClient, 'voidSale').mockReturnValue(of(voidResult));
      const getSpy = vi.spyOn(salesClient, 'getSaleById');

      // Mock dialog service open to return confirmed void
      const mockDialogRef = {
        afterClosed: () => of({ confirmed: true, reason: 'Accidental duplicate entry' })
      } as any;
      vi.spyOn(dialogService, 'open').mockReturnValue(mockDialogRef);

      component.openVoidDialog();

      expect(voidSpy).toHaveBeenCalledWith('sale-111', { reason: 'Accidental duplicate entry' });
      // Verify refresh call
      expect(getSpy).toHaveBeenCalled();
    });

    it('recording payment calls recordPayment endpoint and refreshes sale details', async () => {
      const paymentResult = {
        paymentId: 'pay-2',
        newAmountPaid: 10000,
        newBalance: 0
      };

      const paySpy = vi.spyOn(salesClient, 'recordPayment').mockReturnValue(of(paymentResult));
      const getSpy = vi.spyOn(salesClient, 'getSaleById');

      const mockDialogRef = {
        afterClosed: () => of({ confirmed: true, amount: 4000, paymentMode: 'upi' })
      } as any;
      vi.spyOn(dialogService, 'open').mockReturnValue(mockDialogRef);

      component.openRecordPaymentDialog();

      expect(paySpy).toHaveBeenCalledWith('sale-111', { amount: 4000, paymentMode: 'upi' });
      expect(getSpy).toHaveBeenCalled();
    });
  });
});
