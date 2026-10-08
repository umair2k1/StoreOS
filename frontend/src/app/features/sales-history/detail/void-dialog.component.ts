import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';

export interface VoidDialogData {
  saleId: string;
  invoiceNumber: string;
}

export interface VoidDialogResult {
  confirmed: boolean;
  reason: string;
}

@Component({
  selector: 'app-void-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="void-dialog-panel">
      <div class="dialog-header">
        <h2 class="dialog-title">Void Sale {{ data.invoiceNumber }}</h2>
      </div>

      <div class="dialog-body">
        <div class="warning-banner">
          <span class="warning-icon">&#x26A0;</span>
          <p class="warning-text">
            Voiding is an irreversible action. The sale invoice will be voided and inventory returned.
            A non-empty reason is strictly required.
          </p>
        </div>

        <div class="form-group">
          <label for="voidReasonInput" class="form-label">
            Reason for Void <span class="required">*</span>
          </label>
          <textarea
            id="voidReasonInput"
            class="form-control"
            rows="3"
            placeholder="Enter reason for voiding this sale..."
            [ngModel]="reason()"
            (ngModelChange)="reason.set($event)"
          ></textarea>
          @if (reason().length > 0 && !isValid()) {
            <span class="field-error">Reason cannot be only whitespace.</span>
          }
        </div>
      </div>

      <div class="dialog-footer">
        <button type="button" class="btn btn-secondary" (click)="cancel()">
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-danger btn-confirm-void"
          [disabled]="!isValid()"
          (click)="confirm()"
        >
          Confirm Void
        </button>
      </div>
    </div>
  `,
  styles: [`
    .void-dialog-panel {
      padding: 1.5rem;
      width: 480px;
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
    .warning-banner {
      display: flex;
      gap: 0.75rem;
      background: #fef2f2;
      border: 1px solid #fecaca;
      border-radius: var(--radius-md, 8px);
      padding: 0.875rem;
      margin-bottom: 1rem;
      color: #991b1b;
      font-size: 0.875rem;
    }
    .warning-icon {
      font-size: 1.1rem;
    }
    .warning-text {
      margin: 0;
    }
    .form-group {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
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
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--border, #cbd5e1);
      border-radius: var(--radius-md, 6px);
      font-family: inherit;
      font-size: 0.9rem;
      box-sizing: border-box;
      resize: vertical;
    }
    .form-control:focus {
      outline: none;
      border-color: #dc2626;
      box-shadow: 0 0 0 3px rgba(220, 38, 38, 0.15);
    }
    .field-error {
      color: #dc2626;
      font-size: 0.75rem;
    }
    .dialog-footer {
      display: flex;
      justify-content: flex-end;
      gap: 0.75rem;
      margin-top: 1.5rem;
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
    .btn-secondary:hover {
      background: #e2e8f0;
    }
    .btn-danger {
      background: #dc2626;
      color: #ffffff;
    }
    .btn-danger:hover:not(:disabled) {
      background: #b91c1c;
    }
    .btn-danger:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }
  `]
})
export class VoidDialogComponent {
  private readonly dialogRef = inject(PzDialogRef) as PzDialogRef<VoidDialogComponent, VoidDialogResult>;
  readonly data = inject(PZ_DIALOG_DATA) as VoidDialogData;
  readonly reason = signal<string>('');

  readonly isValid = computed(() => this.reason().trim().length > 0);

  cancel(): void {
    this.dialogRef.close({ confirmed: false, reason: '' });
  }

  confirm(): void {
    if (!this.isValid()) {
      return;
    }
    this.dialogRef.close({ confirmed: true, reason: this.reason().trim() });
  }
}
