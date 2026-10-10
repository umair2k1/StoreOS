import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, Subject, Subscription, of } from 'rxjs';
import { debounceTime, distinctUntilChanged, switchMap, catchError, tap } from 'rxjs';
import { ImportsClient, PurchasesClient, SuppliersClient } from '../../../core/api/generated-client';
import type {
  ImportSupplierInvoiceCommand,
  SupplierInvoiceImportLine,
  StockInCommand,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

export interface ProductSuggestionDto {
  id: string;
  name: string;
  category: string;
  sellPrice?: number;
  purchasePrice?: number;
  hsnCode?: string;
  gstRate?: number;
}

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
  imports: [CommonModule, FormsModule],
  templateUrl: './supplier-invoice-import.component.html',
  styleUrl: './supplier-invoice-import.component.scss'
})
export class SupplierInvoiceImportComponent implements OnInit, OnDestroy {
  private readonly importsClient = inject(ImportsClient);
  private readonly purchasesClient = inject(PurchasesClient);
  private readonly suppliersClient = inject(SuppliersClient);
  private readonly toastService = inject(ToastService);
  private readonly http = inject(HttpClient);

  // Suggestive Product Search Signals
  readonly activeRowIndex = signal<number | null>(null);
  readonly isDropdownOpen = signal<boolean>(false);
  readonly isSearchingProducts = signal<boolean>(false);
  readonly productSuggestions = signal<ProductSuggestionDto[]>([]);

  private readonly searchSubject = new Subject<{ index: number; query: string }>();
  private searchSub?: Subscription;

  // Create Product Modal State
  readonly isCreateProductModalOpen = signal<boolean>(false);
  readonly resolvingLine = signal<AmbiguousLineResolutionItem | null>(null);
  readonly newProductName = signal<string>('');
  readonly newProductCategory = signal<string>('NewPhone');
  readonly newProductPurchasePrice = signal<number>(0);
  readonly newProductSellPrice = signal<number>(0);
  readonly newProductGstRate = signal<number>(18);
  readonly newProductHsnCode = signal<string>('');
  readonly isCreatingProduct = signal<boolean>(false);
  readonly createProductError = signal<string | null>(null);

  readonly isCreateProductValid = computed(() => {
    return (
      this.newProductName().trim().length > 0 &&
      this.newProductCategory().trim().length > 0 &&
      this.newProductPurchasePrice() >= 0 &&
      this.newProductSellPrice() >= 0 &&
      this.newProductGstRate() >= 0
    );
  });

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
    this.initProductSearch();
    this.loadSuppliers();
  }

  ngOnDestroy(): void {
    this.searchSub?.unsubscribe();
  }

  private initProductSearch(): void {
    this.searchSub = this.searchSubject.pipe(
      debounceTime(250),
      distinctUntilChanged((prev, curr) => prev.index === curr.index && prev.query === curr.query),
      switchMap(({ query }) => {
        const term = (query || '').trim();
        if (term.length === 0) {
          this.productSuggestions.set([]);
          this.isDropdownOpen.set(false);
          this.isSearchingProducts.set(false);
          return of([]);
        }
        this.isSearchingProducts.set(true);
        return this.http.get<{ items?: ProductSuggestionDto[] } | ProductSuggestionDto[]>(
          '/api/products',
          { params: { search: term, pageSize: 8 } }
        ).pipe(
          catchError(() => of([])),
          tap(() => this.isSearchingProducts.set(false))
        );
      })
    ).subscribe(resp => {
      let list: ProductSuggestionDto[] = [];
      if (Array.isArray(resp)) {
        list = resp;
      } else if (resp && typeof resp === 'object' && 'items' in resp && Array.isArray(resp.items)) {
        list = resp.items;
      }
      this.productSuggestions.set(list);
      this.isDropdownOpen.set(list.length > 0);
    });
  }

  onProductNameInput(index: number, query: string): void {
    this.activeRowIndex.set(index);
    this.searchSubject.next({ index, query });
  }

  onProductNameFocus(index: number, query: string): void {
    this.activeRowIndex.set(index);
    if (query && query.trim().length > 0) {
      this.searchSubject.next({ index, query });
    }
  }

  onProductNameBlur(): void {
    setTimeout(() => {
      this.isDropdownOpen.set(false);
    }, 200);
  }

  selectProductSuggestion(index: number, product: ProductSuggestionDto): void {
    const currentRows = [...this.rows()];
    if (currentRows[index]) {
      currentRows[index] = {
        ...currentRows[index],
        productNameRaw: product.name,
        hsnCode: currentRows[index].hsnCode || product.hsnCode || '',
        rate: currentRows[index].rate > 0 ? currentRows[index].rate : (product.purchasePrice ?? currentRows[index].rate)
      };
      this.rows.set(currentRows);
    }
    this.isDropdownOpen.set(false);
    this.productSuggestions.set([]);
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

  openCreateProductModal(item: AmbiguousLineResolutionItem): void {
    this.resolvingLine.set(item);
    this.newProductName.set(item.productNameRaw);
    this.newProductCategory.set('NewPhone');
    const rate = item.originalRow?.rate ?? 0;
    this.newProductPurchasePrice.set(rate);
    this.newProductSellPrice.set(rate);
    this.newProductGstRate.set(18);
    this.newProductHsnCode.set(item.originalRow?.hsnCode ?? '');
    this.createProductError.set(null);
    this.isCreateProductModalOpen.set(true);
  }

  closeCreateProductModal(): void {
    this.isCreateProductModalOpen.set(false);
    this.resolvingLine.set(null);
    this.createProductError.set(null);
  }

  async submitCreateProduct(): Promise<void> {
    const item = this.resolvingLine();
    if (!item || !this.isCreateProductValid() || this.isCreatingProduct()) {
      return;
    }

    this.isCreatingProduct.set(true);
    this.createProductError.set(null);

    const command = {
      name: this.newProductName().trim(),
      category: this.newProductCategory().trim(),
      purchasePrice: this.newProductPurchasePrice(),
      sellPrice: this.newProductSellPrice(),
      gstRate: this.newProductGstRate(),
      hsnCode: this.newProductHsnCode().trim() || undefined,
      isMarginScheme: false
    };

    try {
      const resp = await firstValueFrom(
        this.http.post<{ id: string }>('/api/products', command)
      );
      const newProductId = resp?.id;
      if (!newProductId) {
        throw new Error('Product created but no ID was returned.');
      }

      this.toastService.success(`Product "${command.name}" created successfully.`);

      // Automatically assign the newly created product ID and stock in this line
      item.selectedProductId = newProductId;
      await this.retryLine(item);
      this.closeCreateProductModal();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create product.';
      this.createProductError.set(msg);
      this.toastService.error(`Failed to create product: ${msg}`);
    } finally {
      this.isCreatingProduct.set(false);
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
