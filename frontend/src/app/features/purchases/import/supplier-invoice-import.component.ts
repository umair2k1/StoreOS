import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ImportsClient, PurchasesClient, SuppliersClient } from '../../../core/api/generated-client';
import type {
  ImportSupplierInvoiceCommand,
  SupplierInvoiceImportLine,
  StockInCommand,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

export interface ImportGridRow {
  id: string;
  productNameRaw: string;
  hsnCode: string;
  qty: number;
  rate: number;
  imei: string;
}

export interface AmbiguousLineDto {
  productNameRaw: string;
  candidateProductIds: string[];
}

export interface ImportResultDto {
  importBatchId?: string;
  status?: string;
  productsMatched?: number;
  productsAmbiguous?: number;
  ambiguousLines?: AmbiguousLineDto[];
  purchasesCreated?: number;
}

export interface AmbiguousLineResolutionItem {
  productNameRaw: string;
  candidateProductIds: string[];
  selectedProductId: string;
  isResolved: boolean;
  isRetrying: boolean;
  errorMessage: string | null;
  originalRow?: ImportGridRow;
}

export interface SupplierOption {
  id: string;
  name: string;
}

@Component({
  selector: 'app-supplier-invoice-import',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './supplier-invoice-import.component.html',
  styleUrl: './supplier-invoice-import.component.scss'
})
export class SupplierInvoiceImportComponent implements OnInit {
  private readonly importsClient = inject(ImportsClient);
  private readonly purchasesClient = inject(PurchasesClient);
  private readonly suppliersClient = inject(SuppliersClient);
  private readonly toastService = inject(ToastService);

  // Form Header Signals
  readonly supplierId = signal<string>('');
  readonly supplierName = signal<string>('');
  readonly supplierInvoiceNo = signal<string>('');
  readonly invoiceDate = signal<string>(new Date().toISOString().substring(0, 10));
  readonly sourceRef = signal<string>('');

  // Suppliers lookup
  readonly suppliers = signal<SupplierOption[]>([]);
  readonly isLoadingSuppliers = signal<boolean>(false);

  // Repeatable Line Item Grid
  readonly rows = signal<ImportGridRow[]>([
    {
      id: Math.random().toString(36).substring(2, 9),
      productNameRaw: '',
      hsnCode: '',
      qty: 1,
      rate: 0,
      imei: ''
    }
  ]);

  // Submission & Import State
  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly importResult = signal<ImportResultDto | null>(null);
  readonly successSummary = signal<string | null>(null);

  // Ambiguous Lines Resolution
  readonly ambiguousLines = signal<AmbiguousLineResolutionItem[]>([]);

  readonly hasAmbiguousLines = computed(() => this.ambiguousLines().length > 0);
  readonly allAmbiguousResolved = computed(() => {
    const list = this.ambiguousLines();
    return list.length > 0 && list.every(item => item.isResolved);
  });

  readonly isFormValid = computed(() => {
    const hasSupplier = Boolean(this.supplierId().trim() || this.supplierName().trim());
    const hasInvoiceNo = Boolean(this.supplierInvoiceNo().trim());
    const hasDate = Boolean(this.invoiceDate().trim());
    const currentRows = this.rows();
    const hasValidRows = currentRows.length > 0 && currentRows.every(r =>
      r.productNameRaw.trim().length > 0 && r.qty > 0 && r.rate >= 0
    );
    return hasSupplier && hasInvoiceNo && hasDate && hasValidRows;
  });

  ngOnInit(): void {
    this.loadSuppliers();
  }

  async loadSuppliers(): Promise<void> {
    this.isLoadingSuppliers.set(true);
    try {
      const resp = await firstValueFrom(this.suppliersClient.list(undefined, 1, 100));
      const parsed = await this.parseFileResponse<unknown>(resp);
      const loadedOptions: SupplierOption[] = [];

      let rawList: unknown[] = [];
      if (Array.isArray(parsed)) {
        rawList = parsed;
      } else if (parsed && typeof parsed === 'object' && 'items' in parsed) {
        const nestedItems = parsed.items;
        if (Array.isArray(nestedItems)) {
          rawList = nestedItems;
        }
      }

      for (const item of rawList) {
        if (item && typeof item === 'object') {
          let sId = '';
          if ('id' in item && typeof item.id === 'string') {
            sId = item.id;
          } else if ('supplierId' in item && typeof item.supplierId === 'string') {
            sId = item.supplierId;
          }

          let sName = 'Unknown Supplier';
          if ('name' in item && typeof item.name === 'string') {
            sName = item.name;
          } else if ('companyName' in item && typeof item.companyName === 'string') {
            sName = item.companyName;
          }

          if (sId) {
            loadedOptions.push({ id: sId, name: sName });
          }
        }
      }

      this.suppliers.set(loadedOptions);
    } catch {
      // Fallback: manual supplier entry remains functional
    } finally {
      this.isLoadingSuppliers.set(false);
    }
  }

  onSupplierSelect(id: string): void {
    this.supplierId.set(id);
    const matched = this.suppliers().find(s => s.id === id);
    if (matched) {
      this.supplierName.set(matched.name);
    }
  }

  addRow(): void {
    this.rows.update(current => [
      ...current,
      {
        id: Math.random().toString(36).substring(2, 9),
        productNameRaw: '',
        hsnCode: '',
        qty: 1,
        rate: 0,
        imei: ''
      }
    ]);
  }

  removeRow(index: number): void {
    if (this.rows().length <= 1) {
      return;
    }
    this.rows.update(current => current.filter((_, i) => i !== index));
  }

  async submitImport(): Promise<void> {
    if (!this.isFormValid() || this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successSummary.set(null);
    this.ambiguousLines.set([]);

    const invoiceLines: SupplierInvoiceImportLine[] = this.rows().map(r => ({
      productNameRaw: r.productNameRaw.trim(),
      hsnCode: r.hsnCode.trim() || undefined,
      qty: r.qty,
      rate: r.rate,
      imei: r.imei.trim() || undefined
    }));

    const resolvedSupplierId = this.supplierId().trim() || '00000000-0000-0000-0000-000000000000';
    const computedSourceRef = this.sourceRef().trim() ||
      `${this.supplierInvoiceNo().trim()}-${Date.now()}`;

    const command: ImportSupplierInvoiceCommand = {
      supplierId: resolvedSupplierId,
      supplierInvoiceNo: this.supplierInvoiceNo().trim(),
      invoiceDate: new Date(this.invoiceDate()),
      sourceRef: computedSourceRef,
      lines: invoiceLines
    };

    try {
      const resp = await firstValueFrom(this.importsClient.importSupplierInvoice(command));
      const parsed = await this.parseFileResponse<ImportResultDto>(resp);
      this.handleImportSuccess(parsed);
    } catch {
      this.errorMessage.set('Import failed. Please check the provided data and try again.');
      this.toastService.error('Import failed.');
    } finally {
      this.isSubmitting.set(false);
    }
  }

  handleImportSuccess(result: ImportResultDto | null): void {
    this.importResult.set(result);
    const ambiguous = result?.ambiguousLines || [];

    if (ambiguous.length === 0) {
      const matched = result?.productsMatched ?? this.rows().length;
      const created = result?.purchasesCreated ?? this.rows().length;
      this.successSummary.set(
        `Import completed successfully. Matched ${matched} product(s) and created ${created} purchase record(s).`
      );
      this.toastService.success('Invoice imported successfully.');
    } else {
      const resolutionItems: AmbiguousLineResolutionItem[] = ambiguous.map(line => {
        const matchingRow = this.rows().find(r =>
          r.productNameRaw.trim().toLowerCase() === line.productNameRaw.trim().toLowerCase()
        );
        return {
          productNameRaw: line.productNameRaw,
          candidateProductIds: line.candidateProductIds || [],
          selectedProductId: '',
          isResolved: false,
          isRetrying: false,
          errorMessage: null,
          originalRow: matchingRow
        };
      });
      this.ambiguousLines.set(resolutionItems);
      this.toastService.warning(
        `Import processed with ${ambiguous.length} ambiguous line(s) requiring manual confirmation.`
      );
    }
  }

  async retryLine(item: AmbiguousLineResolutionItem): Promise<void> {
    if (!item.selectedProductId || item.isRetrying || item.isResolved) {
      return;
    }

    item.isRetrying = true;
    item.errorMessage = null;

    const row = item.originalRow;
    const stockInCmd: StockInCommand = {
      productId: item.selectedProductId,
      qty: row?.qty ?? 1,
      purchaseRate: row?.rate ?? 0,
      supplierId: this.supplierId().trim() || undefined,
      supplierName: this.supplierName().trim() || undefined,
      supplierInvoiceNo: this.supplierInvoiceNo().trim() || undefined,
      purchaseDate: new Date(this.invoiceDate()),
      imeis: row?.imei?.trim() ? [row.imei.trim()] : undefined
    };

    try {
      await firstValueFrom(this.purchasesClient.stockIn(stockInCmd));
      item.isResolved = true;
      this.toastService.success(`Successfully stocked in line "${item.productNameRaw}".`);

      if (this.allAmbiguousResolved()) {
        this.successSummary.set('All ambiguous lines have been successfully resolved and stocked in.');
      }
    } catch {
      item.errorMessage = `Failed to stock in line "${item.productNameRaw}". Please retry.`;
      this.toastService.error(`Failed to stock in line "${item.productNameRaw}".`);
    } finally {
      item.isRetrying = false;
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp) return null;
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      if (!text) return null;
      const parsedUnknown = JSON.parse(text) as unknown;
      return parsedUnknown as T;
    }
    if (typeof resp.data === 'string') {
      const parsedUnknown = JSON.parse(resp.data) as unknown;
      return parsedUnknown as T;
    }
    const rawData = resp.data as unknown;
    return (rawData as T) || null;
  }
}
