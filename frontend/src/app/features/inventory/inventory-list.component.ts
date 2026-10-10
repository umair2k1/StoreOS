import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink, Params } from '@angular/router';
import { Subscription, firstValueFrom } from 'rxjs';
import { InventoryClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import {
  PzTableComponent,
  PzTableColumn,
  PzTableSortEvent,
  LoadingStateComponent,
  ErrorStateComponent,
  PzMoneyAliasPipe
} from '../../shared';

export interface InventoryLineDto {
  productId: string;
  branchId: string;
  productName: string;
  category: string;
  stockQty: number;
  costPrice: number;
  effectiveSellPrice: number;
  gstRate: number;
  isMarginScheme: boolean;
  hsnCode?: string | null;
  stockStatus: string;
}

export interface InventoryListResponse {
  items: InventoryLineDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

@Component({
  selector: 'app-inventory-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    PzTableComponent,
    ErrorStateComponent
  ],
  templateUrl: './inventory-list.component.html',
  styleUrl: './inventory-list.component.scss'
})
export class InventoryListComponent implements OnInit, OnDestroy {
  private readonly inventoryClient = inject(InventoryClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private querySub?: Subscription;

  // Filter signals bound to URL query params
  readonly search = signal<string>('');
  readonly category = signal<string>('');
  readonly stockStatus = signal<string>('');
  readonly isMarginScheme = signal<string>('all'); // 'all' | 'true' | 'false'

  // Categories lookup
  readonly categories = signal<string[]>([]);
  readonly allCategoryOptions = computed(() => {
    const list = [...this.categories()];
    const current = this.category();
    if (current && !list.includes(current)) {
      list.unshift(current);
    }
    return list;
  });

  readonly sortBy = signal<string>('ProductName');
  readonly sortDirection = signal<'asc' | 'desc'>('asc');
  readonly page = signal<number>(1);
  readonly pageSize = signal<number>(50);

  // Data signals
  readonly items = signal<InventoryLineDto[]>([]);
  readonly totalCount = signal<number>(0);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Bulk selection tracking
  readonly selectedProductIds = signal<Set<string>>(new Set<string>());

  readonly hasSelection = computed(() => this.selectedProductIds().size > 0);
  readonly selectedCount = computed(() => this.selectedProductIds().size);

  readonly totalPages = computed(() => {
    const size = this.pageSize();
    if (size <= 0) return 1;
    return Math.max(1, Math.ceil(this.totalCount() / size));
  });

  readonly columns: PzTableColumn<InventoryLineDto>[] = [
    { key: 'productName', label: 'Product Name', sortable: true },
    { key: 'category', label: 'Category', sortable: true, width: '150px' },
    {
      key: 'stockQty',
      label: 'Stock Qty',
      sortable: true,
      align: 'right',
      width: '120px'
    },
    {
      key: 'stockStatus',
      label: 'Availability',
      sortable: true,
      width: '160px',
      type: 'stock-status'
    },
    {
      key: 'costPrice',
      label: 'Cost Price',
      sortable: false,
      align: 'right',
      width: '130px',
      formatter: (val) => (val !== undefined && val !== null ? `₹${Number(val).toFixed(2)}` : '₹0.00')
    },
    {
      key: 'effectiveSellPrice',
      label: 'Sell Price',
      sortable: true,
      align: 'right',
      width: '130px',
      formatter: (val) => (val !== undefined && val !== null ? `₹${Number(val).toFixed(2)}` : '₹0.00')
    },
    {
      key: 'isMarginScheme',
      label: 'Scheme',
      sortable: false,
      width: '110px',
      formatter: (val) => (val ? 'Margin' : 'Standard')
    }
  ];

  ngOnInit(): void {
    this.loadCategories();
    this.querySub = this.route.queryParams.subscribe((params) => {
      this.syncStateFromParams(params);
      this.fetchInventory();
    });
  }

  async loadCategories(): Promise<void> {
    try {
      const cats = await firstValueFrom(this.inventoryClient.categories());
      if (cats && cats.length > 0) {
        this.categories.set(cats);
      }
    } catch {
      // Fallback: categories will be populated from inventory items
    }
  }

  ngOnDestroy(): void {
    this.querySub?.unsubscribe();
  }

  syncStateFromParams(params: Params): void {
    this.search.set(params['search'] || params['name'] || '');
    this.category.set(params['category'] || '');
    this.stockStatus.set(params['stockStatus'] || '');
    this.isMarginScheme.set(params['isMarginScheme'] || 'all');

    this.sortBy.set(params['sortBy'] || 'ProductName');
    this.sortDirection.set(params['sortDirection'] === 'desc' ? 'desc' : 'asc');
    this.page.set(params['page'] ? Number(params['page']) : 1);
    this.pageSize.set(params['pageSize'] ? Number(params['pageSize']) : 50);
  }

  async fetchInventory(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    const marginParam =
      this.isMarginScheme() === 'true'
        ? true
        : this.isMarginScheme() === 'false'
        ? false
        : undefined;

    try {
      const response = await firstValueFrom(
        this.inventoryClient.list(
          this.category() || undefined,
          this.stockStatus() || undefined,
          marginParam,
          this.search() || undefined,
          undefined, // branchIds
          this.page(),
          this.pageSize()
        )
      );

      const parsed = await this.parseInventoryResponse(response);
      let list = parsed.items || [];

      // Apply client-side sorting if needed
      list = this.applySorting(list, this.sortBy(), this.sortDirection());

      // Merge any categories from loaded items into categories lookup
      const currentCats = new Set(this.categories());
      for (const item of list) {
        if (item.category) {
          currentCats.add(item.category);
        }
      }
      if (currentCats.size > 0) {
        this.categories.set(Array.from(currentCats).sort());
      }

      this.items.set(list);
      this.totalCount.set(parsed.totalCount || list.length);
      this.isLoading.set(false);
    } catch {
      this.isLoading.set(false);
      this.errorMessage.set('Failed to load inventory.');
    }
  }

  updateParam(key: string, value: unknown): void {
    const nextParams: Params = { ...this.route.snapshot.queryParams };

    if (key !== 'page') {
      nextParams['page'] = 1;
    }

    if (value === null || value === undefined || value === '' || value === 'all') {
      delete nextParams[key];
    } else {
      nextParams[key] = value;
    }

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: nextParams
    });
  }

  onSort(event: PzTableSortEvent): void {
    const nextParams: Params = {
      ...this.route.snapshot.queryParams,
      sortBy: event.column,
      sortDirection: event.direction,
      page: 1
    };

    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: nextParams
    });
  }

  onPageChange(newPage: number): void {
    if (newPage < 1 || newPage > this.totalPages()) return;
    this.updateParam('page', newPage);
  }

  onPageSizeChange(newSize: number): void {
    this.updateParam('pageSize', newSize);
  }

  onSelectAll(selected: boolean): void {
    if (selected) {
      const currentIds = this.items().map((i) => i.productId);
      this.selectedProductIds.set(new Set(currentIds));
    } else {
      this.selectedProductIds.set(new Set<string>());
    }
  }

  onSelectRow(event: { row: InventoryLineDto; selected: boolean }): void {
    const next = new Set(this.selectedProductIds());
    if (event.selected) {
      next.add(event.row.productId);
    } else {
      next.delete(event.row.productId);
    }
    this.selectedProductIds.set(next);
  }

  clearSelection(): void {
    this.selectedProductIds.set(new Set<string>());
  }

  exportSelected(): void {
    const ids = Array.from(this.selectedProductIds());
    if (ids.length === 0) return;

    this.router.navigate(['/inventory/export'], {
      queryParams: {
        productIds: ids.join(',')
      }
    });
  }

  onRowClick(row: InventoryLineDto): void {
    if (row.productId) {
      this.router.navigate(['/inventory', row.productId]);
    }
  }

  resetFilters(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {}
    });
  }

  private applySorting(
    list: InventoryLineDto[],
    sortBy: string,
    sortDirection: 'asc' | 'desc'
  ): InventoryLineDto[] {
    const copy = [...list];
    const dir = sortDirection === 'desc' ? -1 : 1;

    return copy.sort((a, b) => {
      const valA = (a as unknown as Record<string, unknown>)[sortBy];
      const valB = (b as unknown as Record<string, unknown>)[sortBy];

      if (valA === valB) return 0;
      if (valA === null || valA === undefined) return 1;
      if (valB === null || valB === undefined) return -1;

      if (typeof valA === 'number' && typeof valB === 'number') {
        return (valA - valB) * dir;
      }
      return String(valA).localeCompare(String(valB)) * dir;
    });
  }

  private async parseInventoryResponse(resp: FileResponse): Promise<InventoryListResponse> {
    if (!resp) return { items: [], totalCount: 0, page: 1, pageSize: 50 };

    let parsed: any = {};
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      if (text) {
        try {
          parsed = JSON.parse(text);
        } catch {}
      }
    } else if (typeof resp.data === 'string') {
      try {
        parsed = JSON.parse(resp.data);
      } catch {}
    } else if (resp.data && typeof resp.data === 'object') {
      parsed = resp.data;
    }

    return {
      items: parsed.items || [],
      totalCount: parsed.totalCount !== undefined ? parsed.totalCount : (parsed.items ? parsed.items.length : 0),
      page: parsed.page || 1,
      pageSize: parsed.pageSize || 50
    };
  }
}
