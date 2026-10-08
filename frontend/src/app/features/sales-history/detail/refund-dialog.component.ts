import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';
import type { SaleDetailDto, SaleLineDto } from '../../../core/api/generated-client';

export interface RefundDialogData {
  sale: SaleDetailDto;
}

export interface RefundDialogResult {
  confirmed: boolean;
  reason: string;
  saleItemIds: string[];
  refundAmount: number;
}

@Component({
  selector: 'app-refund-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="refund-dialog-panel">
      <div class="dialog-header">
        <h2 class="dialog-title">Refund Sale {{ data.sale.invoiceNumber }}</h2>
      </div>

      <div class="dialog-body">
        <p class="section-hint">
          Select items to return for partial refund, or leave unselected to perform a general value refund.
        </p>

        @if (items().length > 0) {
          <div class="items-selection-box">
            <div class="selection-header">
              <span class="col-check">Select</span>
              <span class="col-item">Item</span>
              <span class="col-qty">Qty</span>
              <span class="col-price">Total</span>
            </div>
            <div class="selection-list">
              @for (item of items(); track item.id || item.itemName) {
                <label class="selection-row">
                  <input
                    type="checkbox"
                    [checked]="isItemSelected(item.id!)"
                    (change)="toggleItem(item.id!)"
                  />
                  <span class="item-name">{{ item.itemName }}</span>
                  <span class="item-qty">{{ item.qty }}</span>
                  <span class="item-price">
                    ₹{{ ((item.taxable || 0) + (item.cgst || 0) + (item.sgst || 0)).toFixed(2) }}
                  </span>
                </label>
              }
            </div>
          </div>
        }

        <div class="form-group amount-group">
          <label for="refundAmountInput" class="form-label">
            Refund Amount (₹) <span class="required">*</span>
          </label>
          <input
            id="refundAmountInput"
            type="number"
            class="form-control"
            min="0.01"
            [max]="data.sale.amountPaid || data.sale.grandTotal || 0"
            step="0.01"
            [ngModel]="refundAmount()"
            (ngModelChange)="onAmountInput($event)"
          />
          <span class="help-text">
            Max refundable: ₹{{ (data.sale.amountPaid || data.sale.grandTotal || 0).toFixed(2) }}
          </span>
        </div>

        <div class="form-group">
          <label for="refundReasonInput" class="form-label">
            Reason for Refund <span class="required">*</span>
          </label>
          <textarea
            id="refundReasonInput"
            class="form-control"
            rows="2"
            placeholder="Enter required reason for refund..."
            [ngModel]="reason()"
            (ngModelChange)="reason.set($event)"
          ></textarea>
        </div>
      </div>

      <div class="dialog-footer">
        <button type="button" class="btn btn-secondary" (click)="cancel()">
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-warning btn-confirm-refund"
          [disabled]="!isValid()"
          (click)="confirm()"
        >
          Confirm Refund
        </button>
      </div>
    </div>
  `,
  styles: [`
    .refund-dialog-panel {
      padding: 1.5rem;
      width: 520px;
      max-width: 90vw;
    }
    .dialog-header {
      margin-bottom: 0.75rem;
    }
    .dialog-title {
      margin: 0;
      font-size: 1.25rem;
      font-weight: 700;
      color: var(--fg, #0f172a);
    }
    .section-hint {
      margin: 0 0 1rem 0;
      font-size: 0.85rem;
      color: var(--muted, #64748b);
    }
    .items-selection-box {
      border: 1px solid var(--border, #e2e8f0);
      border-radius: var(--radius-md, 6px);
      margin-bottom: 1rem;
      overflow: hidden;
      font-size: 0.85rem;
    }
    .selection-header {
      display: flex;
      padding: 0.4rem 0.75rem;
      background: #f8fafc;
      font-weight: 600;
      color: var(--muted, #64748b);
      border-bottom: 1px solid var(--border, #e2e8f0);
    }
    .selection-list {
      max-height: 140px;
      overflow-y: auto;
    }
    .selection-row {
      display: flex;
      align-items: center;
      padding: 0.5rem 0.75rem;
      cursor: pointer;
      border-bottom: 1px solid #f1f5f9;
      &:hover {
        background: #f8fafc;
      }
      &:last-child {
        border-bottom: none;
      }
    }
    .col-check { width: 40px; }
    .col-item { flex: 1; }
    .col-qty { width: 50px; text-align: center; }
    .col-price { width: 80px; text-align: right; }

    .item-name { flex: 1; margin-left: 0.5rem; }
    .item-qty { width: 50px; text-align: center; }
    .item-price { width: 80px; text-align: right; font-weight: 600; }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      margin-bottom: 0.875rem;
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
      padding: 0.45rem 0.75rem;
      border: 1px solid var(--border, #cbd5e1);
      border-radius: var(--radius-md, 6px);
      font-family: inherit;
      font-size: 0.9rem;
      box-sizing: border-box;
      &:focus {
        outline: none;
        border-color: #d97706;
        box-shadow: 0 0 0 3px rgba(217, 119, 6, 0.15);
      }
    }
    .help-text {
      font-size: 0.75rem;
      color: var(--muted, #64748b);
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
    .btn-warning {
      background: #d97706;
      color: #ffffff;
      &:hover:not(:disabled) {
        background: #b45309;
      }
      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    }
  `]
})
export class RefundDialogComponent {
  private readonly dialogRef = inject(PzDialogRef) as PzDialogRef<RefundDialogComponent, RefundDialogResult>;
  readonly data = inject(PZ_DIALOG_DATA) as RefundDialogData;

  readonly items = signal<SaleLineDto[]>(this.data?.sale?.items || []);
  readonly selectedItemIds = signal<Set<string>>(new Set());
  readonly refundAmount = signal<number>(
    this.data?.sale?.amountPaid || this.data?.sale?.grandTotal || 0
  );
  readonly reason = signal<string>('');
  readonly isValid = computed(() => {
    return this.reason().trim().length > 0 && this.refundAmount() > 0;
  });

  isItemSelected(id: string): boolean {
    return this.selectedItemIds().has(id);
  }

  toggleItem(id: string): void {
    const next = new Set(this.selectedItemIds());
    if (next.has(id)) {
      next.delete(id);
    } else {
      next.add(id);
    }
    this.selectedItemIds.set(next);

    // If items are selected, calculate sum of selected items
    if (next.size > 0) {
      let sum = 0;
      for (const item of this.items()) {
        if (item.id && next.has(item.id)) {
          sum += (item.taxable || 0) + (item.cgst || 0) + (item.sgst || 0);
        }
      }
      this.refundAmount.set(Math.min(sum, this.data.sale.amountPaid || this.data.sale.grandTotal || 0));
    }
  }

  onAmountInput(val: number): void {
    this.refundAmount.set(Number(val) || 0);
  }

  cancel(): void {
    this.dialogRef.close({
      confirmed: false,
      reason: '',
      saleItemIds: [],
      refundAmount: 0
    });
  }

  confirm(): void {
    if (!this.isValid()) {
      return;
    }
    this.dialogRef.close({
      confirmed: true,
      reason: this.reason().trim(),
      saleItemIds: Array.from(this.selectedItemIds()),
      refundAmount: this.refundAmount()
    });
  }
}
