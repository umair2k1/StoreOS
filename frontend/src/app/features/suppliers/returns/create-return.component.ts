import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SupplierReturnsClient } from '../../../core/api/generated-client';
import type {
  CreateSupplierReturnCommand,
  ReturnLineInput
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

export interface ReturnRowState {
  id: string;
  productId: string;
  itemName: string;
  qty: number;
  purchasePrice: number;
  returnRate: number;
  imeiInputs: string[];
}

@Component({
  selector: 'app-create-return',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './create-return.component.html',
  styleUrl: './create-return.component.scss'
})
export class CreateReturnComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly returnsClient = inject(SupplierReturnsClient);
  private readonly toastService = inject(ToastService);

  readonly supplierId = signal<string>('');
  readonly returnDate = signal<string>(new Date().toISOString().substring(0, 10));
  readonly settlementType = signal<string>('CreditNote');
  readonly reason = signal<string>('Defective');
  readonly notes = signal<string>('');

  readonly rows = signal<ReturnRowState[]>([this.createEmptyRow()]);

  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly isValid = computed<boolean>(() => {
    const hasSupplier = Boolean(this.supplierId().trim());
    const hasDate = Boolean(this.returnDate().trim());
    const hasType = Boolean(this.settlementType().trim());
    const currentRows = this.rows();
    const hasValidRows = currentRows.length > 0 && currentRows.every(r =>
      r.itemName.trim().length > 0 && r.qty > 0 && r.returnRate >= 0
    );
    return hasSupplier && hasDate && hasType && hasValidRows;
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.supplierId.set(id);
    } else {
      this.errorMessage.set('Missing supplier ID in route.');
    }
  }

  createEmptyRow(): ReturnRowState {
    return {
      id: Math.random().toString(36).substring(2, 9),
      productId: '',
      itemName: '',
      qty: 1,
      purchasePrice: 0,
      returnRate: 0,
      imeiInputs: []
    };
  }

  addRow(): void {
    this.rows.update(current => [...current, this.createEmptyRow()]);
  }

  removeRow(index: number): void {
    if (this.rows().length <= 1) return;
    this.rows.update(current => current.filter((_, i) => i !== index));
  }

  addImeiToRow(rowIndex: number): void {
    this.rows.update(current => {
      const copy = [...current];
      copy[rowIndex] = {
        ...copy[rowIndex],
        imeiInputs: [...copy[rowIndex].imeiInputs, '']
      };
      return copy;
    });
  }

  removeImeiFromRow(rowIndex: number, imeiIndex: number): void {
    this.rows.update(current => {
      const copy = [...current];
      copy[rowIndex] = {
        ...copy[rowIndex],
        imeiInputs: copy[rowIndex].imeiInputs.filter((_, i) => i !== imeiIndex)
      };
      return copy;
    });
  }

  updateImeiValue(rowIndex: number, imeiIndex: number, val: string): void {
    this.rows.update(current => {
      const copy = [...current];
      const imeis = [...copy[rowIndex].imeiInputs];
      imeis[imeiIndex] = val;
      copy[rowIndex] = { ...copy[rowIndex], imeiInputs: imeis };
      return copy;
    });
  }

  async submitReturn(): Promise<void> {
    if (!this.isValid() || this.isSubmitting()) return;

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const formattedLines: ReturnLineInput[] = this.rows().map(r => {
      const filteredImeis = r.imeiInputs.map(i => i.trim()).filter(i => i.length > 0);
      return {
        productId: r.productId.trim() || undefined,
        itemName: r.itemName.trim(),
        qty: r.qty,
        purchasePrice: r.purchasePrice,
        returnRate: r.returnRate,
        imeiNumbers: filteredImeis.length > 0 ? filteredImeis : undefined
      };
    });

    const command: CreateSupplierReturnCommand = {
      supplierId: this.supplierId().trim(),
      returnDate: new Date(this.returnDate()),
      lines: formattedLines,
      settlementType: this.settlementType(),
      reason: this.reason().trim() || undefined,
      notes: this.notes().trim() || undefined
    };

    try {
      await firstValueFrom(this.returnsClient.create(command));
      this.toastService.success('Supplier return created successfully.');
      this.router.navigate(['/suppliers', this.supplierId()]);
    } catch {
      this.errorMessage.set('Failed to create supplier return.');
      this.toastService.error('Failed to create return.');
    } finally {
      this.isSubmitting.set(false);
    }
  }
}
