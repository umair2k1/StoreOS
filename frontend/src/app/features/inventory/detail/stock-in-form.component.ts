import { Component, Input, Output, EventEmitter, inject, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { PurchasesClient } from '../../../core/api/generated-client';
import type { StockInCommand } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

@Component({
  selector: 'app-stock-in-form',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './stock-in-form.component.html',
  styleUrl: './stock-in-form.component.scss'
})
export class StockInFormComponent {
  private readonly purchasesClient = inject(PurchasesClient);
  private readonly toastService = inject(ToastService);

  @Input() productId = '';
  @Input() set productCategory(val: string | undefined) {
    this._category.set(val || '');
  }

  @Output() stockInCompleted = new EventEmitter<void>();

  private readonly _category = signal<string>('');

  // Form signals
  readonly qty = signal<number>(1);
  readonly purchaseRate = signal<number>(0);
  readonly supplierName = signal<string>('');
  readonly supplierInvoiceNo = signal<string>('');
  readonly isSubmitting = signal<boolean>(false);

  // Dynamic IMEI list
  readonly imeis = signal<string[]>(['']);

  readonly isImeiTracked = computed(() => {
    const cat = this._category().toLowerCase().replace(/[\s_-]/g, '');
    return cat === 'newphone' || cat === 'usedphone' || cat.includes('phone');
  });

  readonly hasDuplicates = computed(() => {
    if (!this.isImeiTracked()) return false;
    const values = this.imeis().map(i => i.trim().toLowerCase()).filter(Boolean);
    return new Set(values).size !== values.length;
  });

  readonly isImeisComplete = computed(() => {
    if (!this.isImeiTracked()) return true;
    const required = this.qty();
    const current = this.imeis();
    if (current.length !== required) return false;
    return current.every(i => i.trim().length > 0);
  });

  readonly isValid = computed(() => {
    return (
      this.qty() >= 1 &&
      this.purchaseRate() > 0 &&
      !this.hasDuplicates() &&
      this.isImeisComplete()
    );
  });

  constructor() {
    // Live synchronization: keep imeis length exactly matching qty
    effect(() => {
      const targetQty = Math.max(1, Math.floor(this.qty() || 1));
      const current = this.imeis();

      if (current.length !== targetQty) {
        if (current.length < targetQty) {
          const added = Array(targetQty - current.length).fill('');
          this.imeis.set([...current, ...added]);
        } else {
          this.imeis.set(current.slice(0, targetQty));
        }
      }
    });
  }

  onQtyChange(newVal: number): void {
    const sanitized = Math.max(1, Math.floor(newVal || 1));
    this.qty.set(sanitized);
  }

  updateImei(index: number, value: string): void {
    const list = [...this.imeis()];
    list[index] = value;
    this.imeis.set(list);
  }

  isDuplicate(index: number): boolean {
    if (!this.isImeiTracked()) return false;
    const val = (this.imeis()[index] || '').trim().toLowerCase();
    if (!val) return false;

    return this.imeis().some(
      (other, idx) => idx !== index && other.trim().toLowerCase() === val
    );
  }

  async submitStockIn(): Promise<void> {
    if (!this.isValid() || this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);

    const command: StockInCommand = {
      productId: this.productId,
      qty: this.qty(),
      purchaseRate: this.purchaseRate(),
      supplierName: this.supplierName().trim() || undefined,
      supplierInvoiceNo: this.supplierInvoiceNo().trim() || undefined,
      purchaseDate: new Date(),
      imeis: this.isImeiTracked() ? this.imeis().map(i => i.trim()) : undefined
    };

    try {
      await firstValueFrom(this.purchasesClient.stockIn(command));
      this.toastService.success(`Successfully stocked in ${this.qty()} unit(s).`);
      this.resetForm();
      this.stockInCompleted.emit();
    } catch {
      this.toastService.error('Failed to submit stock-in.');
    } finally {
      this.isSubmitting.set(false);
    }
  }

  resetForm(): void {
    this.qty.set(1);
    this.purchaseRate.set(0);
    this.supplierName.set('');
    this.supplierInvoiceNo.set('');
    this.imeis.set(['']);
  }
}
