import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ExportsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

export const INVENTORY_CATEGORIES: readonly string[] = [
  'MobilePhone',
  'Tablet',
  'Laptop',
  'Accessory',
  'SparePart',
  'Wearable',
  'RepairService'
];

@Component({
  selector: 'app-exports',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './exports.component.html',
  styleUrl: './exports.component.scss'
})
export class ExportsComponent {
  private readonly exportsClient = inject(ExportsClient);
  private readonly toastService = inject(ToastService);

  readonly availableCategories = INVENTORY_CATEGORIES;

  // Inventory Export State
  readonly selectedCategories = signal<string[]>(['MobilePhone', 'Accessory']);
  readonly isExportingInventory = signal<boolean>(false);

  // GSTR-1 Export State
  readonly gstr1From = signal<string>(this.computeFirstOfMonth());
  readonly gstr1To = signal<string>(this.computeToday());
  readonly isExportingGstr1 = signal<boolean>(false);

  computeFirstOfMonth(): string {
    const d = new Date();
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    return `${yyyy}-${mm}-01`;
  }

  computeToday(): string {
    const d = new Date();
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  toggleCategory(cat: string): void {
    this.selectedCategories.update(current =>
      current.includes(cat) ? current.filter(c => c !== cat) : [...current, cat]
    );
  }

  isCategorySelected(cat: string): boolean {
    return this.selectedCategories().includes(cat);
  }

  async downloadInventoryExport(): Promise<void> {
    const cats = this.selectedCategories();
    this.isExportingInventory.set(true);
    try {
      const resp = await firstValueFrom(
        this.exportsClient.exportInventory(cats.length > 0 ? cats : null, null, 'csv')
      );
      this.triggerBlobDownload(resp, `inventory-export-${this.computeToday()}.csv`);
      this.toastService.success('Inventory export downloaded.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to export inventory.';
      this.toastService.error(msg);
    } finally {
      this.isExportingInventory.set(false);
    }
  }

  async downloadGstr1Export(): Promise<void> {
    const fromStr = this.gstr1From();
    const toStr = this.gstr1To();

    if (!fromStr || !toStr) {
      this.toastService.error('Please select both From and To dates.');
      return;
    }

    this.isExportingGstr1.set(true);
    try {
      const fromDate = new Date(fromStr);
      const toDate = new Date(toStr);
      const resp = await firstValueFrom(
        this.exportsClient.exportGstr1(fromDate, toDate, 'csv')
      );
      this.triggerBlobDownload(resp, `gstr1-${fromStr}-${toStr}.csv`);
      this.toastService.success('GSTR-1 report downloaded.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to export GSTR-1.';
      this.toastService.error(msg);
    } finally {
      this.isExportingGstr1.set(false);
    }
  }

  private triggerBlobDownload(resp: FileResponse, defaultFileName: string): void {
    if (!resp || !resp.data) return;
    const blob = resp.data instanceof Blob ? resp.data : new Blob([String(resp.data)], { type: 'text/csv' });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = resp.fileName || defaultFileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
  }
}
