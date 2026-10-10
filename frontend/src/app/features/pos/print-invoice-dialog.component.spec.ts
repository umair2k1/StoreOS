import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PrintInvoiceDialogComponent } from './print-invoice-dialog.component';
import type { PrintInvoiceDialogData } from './print-invoice-dialog.component';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../shared/components/pz-dialog/pz-dialog.service';

describe('PrintInvoiceDialogComponent', () => {
  let fixture: ComponentFixture<PrintInvoiceDialogComponent>;
  let component: PrintInvoiceDialogComponent;
  let mockDialogRef: { close: ReturnType<typeof vi.fn> };

  const mockData: PrintInvoiceDialogData = {
    saleId: 'sale-001',
    invoiceNumber: 'INV-2026-001'
  };

  beforeEach(async () => {
    mockDialogRef = {
      close: vi.fn()
    };

    await TestBed.configureTestingModule({
      imports: [PrintInvoiceDialogComponent],
      providers: [
        { provide: PzDialogRef, useValue: mockDialogRef },
        { provide: PZ_DIALOG_DATA, useValue: mockData }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(PrintInvoiceDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders invoice number in dialog message', () => {
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('INV-2026-001');
    expect(el.textContent).toContain('Would you like to print out a copy of the invoice?');
  });

  it('closes dialog with true when Print Invoice button is clicked', () => {
    const printBtn = fixture.nativeElement.querySelector('.btn-print') as HTMLButtonElement;
    expect(printBtn).toBeTruthy();
    printBtn.click();
    expect(mockDialogRef.close).toHaveBeenCalledWith(true);
  });

  it('closes dialog with false when No, thanks button is clicked', () => {
    const cancelBtn = fixture.nativeElement.querySelector('.btn-cancel') as HTMLButtonElement;
    expect(cancelBtn).toBeTruthy();
    cancelBtn.click();
    expect(mockDialogRef.close).toHaveBeenCalledWith(false);
  });
});
