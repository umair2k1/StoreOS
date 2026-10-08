import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OverlayModule } from '@angular/cdk/overlay';
import { of } from 'rxjs';
import { PaymentPanelComponent } from './payment-panel.component';
import { FinanceDialogComponent } from './finance-dialog.component';
import { PzDialogService } from '../../../shared/components/pz-dialog/pz-dialog.service';
import type { PzDialogRef } from '../../../shared/components/pz-dialog/pz-dialog.service';
import type { CheckoutFinanceDetails } from '../../../core/api/generated-client';

describe('PaymentPanelComponent', () => {
  let fixture: ComponentFixture<PaymentPanelComponent>;
  let component: PaymentPanelComponent;
  let dialogService: PzDialogService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PaymentPanelComponent, OverlayModule],
      providers: [PzDialogService]
    }).compileComponents();

    dialogService = TestBed.inject(PzDialogService);
    fixture = TestBed.createComponent(PaymentPanelComponent);
    component = fixture.componentInstance;
    component.grandTotal = 5000;
    fixture.detectChanges();
  });

  it('supports split-payment summation across multiple modes', () => {
    // Start with 1 line (Cash: 0), grand total 5000 -> remaining 5000
    expect(component.remainingBalance()).toBe(5000);
    expect(component.totalPaid()).toBe(0);

    // Update Line 1 to Cash: 3000
    const line1Id = component.payments()[0].id;
    component.updateAmount(line1Id, 3000);
    expect(component.totalPaid()).toBe(3000);
    expect(component.remainingBalance()).toBe(2000);

    // Add Line 2 (UPI: 2000)
    component.addPaymentLine('upi', 2000);
    expect(component.payments().length).toBe(2);
    expect(component.totalPaid()).toBe(5000);
    expect(component.remainingBalance()).toBe(0);
    expect(component.changeDue()).toBe(0);
  });

  it('calculates change due correctly upon overpayment with Cash', () => {
    const lineId = component.payments()[0].id;
    component.updateAmount(lineId, 6000);

    expect(component.totalPaid()).toBe(6000);
    expect(component.remainingBalance()).toBe(0);
    expect(component.changeDue()).toBe(1000);
  });

  it('adding and removing payment lines updates state and keeps at least one line', () => {
    component.addPaymentLine('card', 1500);
    expect(component.payments().length).toBe(2);

    const secondLineId = component.payments()[1].id;
    component.removePaymentLine(secondLineId);
    expect(component.payments().length).toBe(1);

    // Removing the only line resets it to 0 amount
    const firstLineId = component.payments()[0].id;
    component.updateAmount(firstLineId, 2500);
    component.removePaymentLine(firstLineId);

    expect(component.payments().length).toBe(1);
    expect(component.payments()[0].amount).toBe(0);
  });

  it('fillRemaining fills the exact remaining balance into the target line', () => {
    component.grandTotal = 4500;
    const lineId = component.payments()[0].id;
    component.updateAmount(lineId, 1500);
    expect(component.remainingBalance()).toBe(3000);

    component.fillRemaining(lineId);
    expect(component.payments()[0].amount).toBe(4500);
    expect(component.remainingBalance()).toBe(0);
  });

  it('opens FinanceDialogComponent and applies financed principal and down payment', () => {
    const mockFinanceResult: CheckoutFinanceDetails = {
      financeProvider: 'bajaj',
      financerName: 'Executive',
      downPayment: 1000,
      tenureMonths: 12,
      annualInterestRate: 0
    };

    // Mock partial PzDialogRef for test assertion
    const mockDialogRef = {
      afterClosed: () => of(mockFinanceResult)
    } as unknown as PzDialogRef<unknown, unknown>;
    const dialogSpy = vi.spyOn(dialogService, 'open').mockReturnValue(mockDialogRef);
    component.openFinanceDialog();

    expect(dialogSpy).toHaveBeenCalledWith(
      FinanceDialogComponent,
      expect.objectContaining({
        data: expect.objectContaining({ grandTotal: 5000 })
      })
    );

    // Grand total: 5000, Down payment: 1000 -> Financed principal: 4000
    expect(component.financeDetails()).toEqual(mockFinanceResult);
    expect(component.financedPrincipal()).toBe(4000);
    // Down payment set on first payment line: 1000 -> remaining balance is 0
    expect(component.remainingBalance()).toBe(0);

    // Remove finance restores unfinanced balance
    component.removeFinance();
    expect(component.financeDetails()).toBeNull();
    expect(component.financedPrincipal()).toBe(0);
    expect(component.remainingBalance()).toBe(4000);
  });
});
