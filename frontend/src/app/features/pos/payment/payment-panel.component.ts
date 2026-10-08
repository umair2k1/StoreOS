import {
  Component,
  Input,
  Output,
  EventEmitter,
  signal,
  computed,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PzDialogService } from '../../../shared/components/pz-dialog/pz-dialog.service';
import { PzMoneyAliasPipe } from '../../../shared/pipes/money.pipe';
import type { CheckoutFinanceDetails, CheckoutPayment } from '../../../core/api/generated-client';
import { FinanceDialogComponent } from './finance-dialog.component';

export type PaymentMode = 'cash' | 'upi' | 'card' | 'credit';

export interface PaymentLine {
  id: string;
  mode: PaymentMode;
  amount: number;
}

@Component({
  selector: 'app-payment-panel',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    PzMoneyAliasPipe
  ],
  templateUrl: './payment-panel.component.html',
  styleUrl: './payment-panel.component.scss'
})
export class PaymentPanelComponent {
  private readonly dialogService = inject(PzDialogService);

  @Input() set grandTotal(val: number) {
    this._grandTotal.set(val);
  }
  get grandTotal(): number {
    return this._grandTotal();
  }
  private readonly _grandTotal = signal<number>(0);

  @Input() set invoiceDiscount(val: number) {
    this._invoiceDiscount.set(val);
  }
  get invoiceDiscount(): number {
    return this._invoiceDiscount();
  }
  private readonly _invoiceDiscount = signal<number>(0);

  @Output() readonly paymentsChange = new EventEmitter<CheckoutPayment[]>();
  @Output() readonly financeChange = new EventEmitter<CheckoutFinanceDetails | null>();

  readonly availableModes: ReadonlyArray<{ value: PaymentMode; label: string }> = [
    { value: 'cash', label: 'Cash' },
    { value: 'upi', label: 'UPI' },
    { value: 'card', label: 'Card' },
    { value: 'credit', label: 'Store Credit' }
  ];

  readonly payments = signal<PaymentLine[]>([
    { id: crypto.randomUUID(), mode: 'cash', amount: 0 }
  ]);

  readonly financeDetails = signal<CheckoutFinanceDetails | null>(null);

  readonly totalPayable = computed(() =>
    Math.max(0, this._grandTotal() - this._invoiceDiscount())
  );

  readonly financedPrincipal = computed(() => {
    const f = this.financeDetails();
    if (!f) {
      return 0;
    }
    const payable = this.totalPayable();
    const dp = f.downPayment ?? 0;
    return Math.max(0, payable - dp);
  });

  readonly totalPaid = computed(() =>
    this.payments().reduce((sum, p) => sum + (p.amount || 0), 0)
  );

  readonly remainingBalance = computed(() => {
    const payable = this.totalPayable();
    const financed = this.financedPrincipal();
    const paid = this.totalPaid();
    return Math.max(0, payable - financed - paid);
  });

  readonly changeDue = computed(() => {
    const payable = this.totalPayable();
    const financed = this.financedPrincipal();
    const target = payable - financed;
    const paid = this.totalPaid();
    return paid > target ? paid - target : 0;
  });

  addPaymentLine(mode: PaymentMode = 'cash', amount?: number): void {
    const defaultAmount = amount !== undefined ? amount : this.remainingBalance();
    const newLine: PaymentLine = {
      id: crypto.randomUUID(),
      mode,
      amount: Math.max(0, defaultAmount)
    };
    const updated = [...this.payments(), newLine];
    this.payments.set(updated);
    this.emitPayments(updated);
  }

  removePaymentLine(id: string): void {
    const current = this.payments();
    if (current.length <= 1) {
      // Keep at least one line reset to 0
      const reset = [{ id: crypto.randomUUID(), mode: 'cash' as PaymentMode, amount: 0 }];
      this.payments.set(reset);
      this.emitPayments(reset);
      return;
    }
    const updated = current.filter(p => p.id !== id);
    this.payments.set(updated);
    this.emitPayments(updated);
  }

  updateAmount(id: string, amount: number): void {
    const updated = this.payments().map(p =>
      p.id === id ? { ...p, amount: Math.max(0, amount) } : p
    );
    this.payments.set(updated);
    this.emitPayments(updated);
  }

  updateMode(id: string, mode: PaymentMode): void {
    const updated = this.payments().map(p =>
      p.id === id ? { ...p, mode } : p
    );
    this.payments.set(updated);
    this.emitPayments(updated);
  }

  fillRemaining(id: string): void {
    const remaining = this.remainingBalance();
    const line = this.payments().find(p => p.id === id);
    if (line) {
      this.updateAmount(id, line.amount + remaining);
    }
  }

  openFinanceDialog(): void {
    const dialogRef = this.dialogService.open<FinanceDialogComponent, unknown, CheckoutFinanceDetails | null>(
      FinanceDialogComponent,
      {
        data: {
          grandTotal: this.totalPayable(),
          initialDownPayment: this.financeDetails()?.downPayment ?? this.totalPaid(),
          initialTenureMonths: this.financeDetails()?.tenureMonths ?? 12,
          initialInterestRate: this.financeDetails()?.annualInterestRate ?? 0
        }
      }
    );

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.financeDetails.set(result);
        this.financeChange.emit(result);

        // If down payment is specified, align the cash/payment line
        if (result.downPayment !== undefined && result.downPayment > 0) {
          const current = this.payments();
          if (current.length === 1 && current[0].amount === 0) {
            this.updateAmount(current[0].id, result.downPayment);
          }
        }
      }
    });
  }

  removeFinance(): void {
    this.financeDetails.set(null);
    this.financeChange.emit(null);
  }

  private emitPayments(lines: PaymentLine[]): void {
    const modeMap: Record<PaymentMode, string> = { cash: 'Cash', upi: 'UPI', card: 'Card', credit: 'Credit' };
    const mapped: CheckoutPayment[] = lines
      .filter(l => l.amount > 0)
      .map(l => ({
        paymentMode: modeMap[l.mode] ?? 'Cash',
        amount: l.amount
      }));
    this.paymentsChange.emit(mapped);
  }
}
