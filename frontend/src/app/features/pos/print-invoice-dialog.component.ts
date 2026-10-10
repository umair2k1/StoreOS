import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { PzDialogComponent } from '../../shared/components/pz-dialog/pz-dialog.component';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../shared/components/pz-dialog/pz-dialog.service';

export interface PrintInvoiceDialogData {
  saleId: string;
  invoiceNumber: string;
}

@Component({
  selector: 'app-print-invoice-dialog',
  standalone: true,
  imports: [CommonModule, PzDialogComponent],
  template: `
    <pz-dialog title="Print Invoice" [showCloseButton]="true" (closed)="onCancel()">
      <div class="print-dialog-content">
        <p class="print-message">
          Sale completed successfully (Invoice #<strong>{{ data.invoiceNumber }}</strong>).
        </p>
        <p class="print-question">
          Would you like to print out a copy of the invoice?
        </p>
      </div>

      <div pz-dialog-actions class="dialog-actions">
        <button type="button" class="btn btn-secondary btn-cancel" (click)="onCancel()">
          No, thanks
        </button>
        <button type="button" class="btn btn-primary btn-print" (click)="onConfirm()">
          Print Invoice
        </button>
      </div>
    </pz-dialog>
  `,
  styles: [`
    .print-dialog-content {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      padding: 0.5rem 0;
      color: var(--fg, #0f172a);
    }

    .print-message {
      margin: 0;
      font-size: 0.95rem;
    }

    .print-question {
      margin: 0;
      font-size: 0.9rem;
      color: var(--muted, #64748b);
    }

    .dialog-actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.75rem;
      margin-top: 1rem;

      .btn {
        padding: 0.5rem 1rem;
        border-radius: var(--radius-md, 8px);
        font-size: 0.875rem;
        font-weight: 500;
        cursor: pointer;
        border: 1px solid transparent;
        transition: all 0.15s ease;

        &.btn-secondary {
          background: var(--surface, #ffffff);
          border-color: var(--border, #e2e8f0);
          color: var(--fg, #0f172a);

          &:hover {
            background: var(--surface-hover, #f1f5f9);
          }
        }

        &.btn-primary {
          background: var(--accent, #2563eb);
          color: #ffffff;

          &:hover {
            background: var(--accent-hover, #1d4ed8);
          }
        }
      }
    }
  `]
})
export class PrintInvoiceDialogComponent {
  readonly dialogRef = inject(PzDialogRef);
  readonly data = inject<PrintInvoiceDialogData>(PZ_DIALOG_DATA);

  onCancel(): void {
    this.dialogRef.close(false);
  }

  onConfirm(): void {
    this.dialogRef.close(true);
  }
}
