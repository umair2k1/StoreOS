import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';

export interface RecordPaymentDialogData {
  saleId: string;
  invoiceNumber: string;
  balance: number;
}

export interface RecordPaymentDialogResult {
  confirmed: boolean;
  amount: number;
  paymentMode: string;
}

@Component({
  selector: 'app-record-payment-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="record-payment-dialog-panel">
      <div class="dialog-header">
        <h2 class="dialog-title">Record Payment for {{ data.invoiceNumber }}</h2>
      </div>

      <div class="dialog-body">
        <div class="balance-banner">
          <span class="label">Outstanding Balance:</span>
          <span class="amount">₹{{ data.balance.toFixed(2) }}</span>
        </div>

        <div class="form-group">
          <label for="paymentAmountInput" class="form-label">
            Payment Amount (₹) <span class="required">*</span>
          </label>
          <input
            id="paymentAmountInput"
            type="number"
            class="form-control"
            min="0.01"
            [max]="data.balance"
            step="0.01"
            [ngModel]="amount()"
            (ngModelChange)="amount.set(+$event)"
          />
        </div>

        <div class="form-group">
          <label for="paymentModeSelect" class="form-label">
            Payment Mode <span class="required">*</span>
          </label>
          <select
            id="paymentModeSelect"
            class="form-control"
            [ngModel]="paymentMode()"
            (ngModelChange)="paymentMode.set($event)"
          >
            <option value="cash">Cash</option>
            <option value="upi">UPI</option>
            <option value="card">Card</option>
            <option value="credit">Store Credit</option>
          </select>
        </div>
      </div>

      <div class="dialog-footer">
        <button type="button" class="btn btn-secondary" (click)="cancel()">
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary btn-confirm-payment"
          [disabled]="!isValid()"
          (click)="confirm()"
        >
          Record Payment
        </button>
      </div>
    </div>
  `,
  styles: [`
    .record-payment-dialog-panel {
      padding: 1.5rem;
      width: 440px;
      max-width: 90vw;
    }
    .dialog-header {
      margin-bottom: 1rem;
    }
    .dialog-title {
      margin: 0;
      font-size: 1.25rem;
      font-weight: 700;
      color: var(--fg, #0f172a);
    }
    .balance-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      background: #eff6ff;
      border: 1px solid #bfdbfe;
      border-radius: var(--radius-md, 8px);
      padding: 0.875rem 1rem;
      margin-bottom: 1rem;
      .label {
        color: #1e40af;
        font-weight: 500;
        font-size: 0.9rem;
      }
      .amount {
        color: #1d4ed8;
        font-weight: 700;
        font-size: 1.1rem;
      }
    }
    .form-group {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      margin-bottom: 1rem;
    }
    .form-label {
      font-size: 0.85rem;
      font-weight: 600;
      color: var(--fg, #0f172a);
    }
    .required {
      color: #dc2626;
    }
    .form-control {
      width: 100%;
      height: 38px;
      padding: 0.4rem 0.75rem;
      border: 1px solid var(--border, #cbd5e1);
      border-radius: var(--radius-md, 6px);
      font-size: 0.9rem;
      box-sizing: border-box;
      &:focus {
        outline: none;
        border-color: var(--accent, #2563eb);
        box-shadow: 0 0 0 3px rgba(37, 99, 235, 0.15);
      }
    }
    .dialog-footer {
      display: flex;
      justify-content: flex-end;
      gap: 0.75rem;
      margin-top: 1.25rem;
    }
    .btn {
      padding: 0.5rem 1rem;
      border-radius: var(--radius-md, 6px);
      font-size: 0.9rem;
      font-weight: 600;
      cursor: pointer;
      border: none;
    }
    .btn-secondary {
      background: #f1f5f9;
      color: var(--fg, #0f172a);
    }
    .btn-primary {
      background: var(--accent, #2563eb);
      color: #ffffff;
      &:hover:not(:disabled) {
        background: var(--accent-hover, #1d4ed8);
      }
      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    }
  `]
})
export class RecordPaymentDialogComponent {
  private readonly dialogRef = inject(PzDialogRef) as PzDialogRef<RecordPaymentDialogComponent, RecordPaymentDialogResult>;
  readonly data = inject(PZ_DIALOG_DATA) as RecordPaymentDialogData;

  readonly amount = signal<number>(this.data?.balance || 0);
  readonly paymentMode = signal<string>('cash');

  readonly isValid = computed(() => {
    const val = this.amount();
    return val > 0 && val <= this.data.balance;
  });

  cancel(): void {
    this.dialogRef.close({ confirmed: false, amount: 0, paymentMode: '' });
  }

  confirm(): void {
    if (!this.isValid()) {
      return;
    }
    this.dialogRef.close({
      confirmed: true,
      amount: this.amount(),
      paymentMode: this.paymentMode()
    });
  }
}
