import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, Params } from '@angular/router';
import { Subscription } from 'rxjs';
import { SalesClient } from '../../core/api/generated-client';
import type { SaleSummaryDto, PagedResultOfSaleSummaryDto } from '../../core/api/generated-client';
import {
  PzTableComponent,
  PzTableColumn,
  PzTableSortEvent,
  ErrorStateComponent
} from '../../shared';

export interface SalesFilterState {
  search: string;
  dateFilter: string;
  customFrom: string;
  customTo: string;
  status: string;
  paymentMode: string;
  invoiceType: string;
  paidOnly: boolean;
  dueOnly: boolean;
  financedOnly: boolean;
  category: string;
  minAmount: number | null;
  maxAmount: number | null;
  sortBy: string;
  sortDirection: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

@Component({
  selector: 'app-sales-history',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    PzTableComponent,
    ErrorStateComponent
  ],
  templateUrl: './sales-history.component.html',
  styleUrl: './sales-history.component.scss'
})
export class SalesHistoryComponent implements OnInit, OnDestroy {
  private readonly salesClient = inject(SalesClient);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  private querySub?: Subscription;

  // Filter signals bound to UI
  readonly search = signal<string>('');
  readonly dateFilter = signal<string>('all');
  readonly customFrom = signal<string>('');
  readonly customTo = signal<string>('');
  readonly status = signal<string>('');
  readonly paymentMode = signal<string>('');
  readonly invoiceType = signal<string>('');
  readonly paidOnly = signal<boolean>(false);
  readonly dueOnly = signal<boolean>(false);
  readonly financedOnly = signal<boolean>(false);
  readonly category = signal<string>('');
  readonly minAmount = signal<number | null>(null);
  readonly maxAmount = signal<number | null>(null);

  readonly sortBy = signal<string>('SaleDate');
  readonly sortDirection = signal<'asc' | 'desc'>('desc');
  readonly page = signal<number>(1);
  readonly pageSize = signal<number>(50);

  // Data signals
  readonly sales = signal<SaleSummaryDto[]>([]);
  readonly totalCount = signal<number>(0);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly totalPages = computed(() => {
    const size = this.pageSize();
    if (size <= 0) return 1;
    return Math.max(1, Math.ceil(this.totalCount() / size));
  });

  readonly columns: PzTableColumn<SaleSummaryDto>[] = [
    { key: 'invoiceNumber', label: 'Invoice #', sortable: true, width: '150px' },
    {
      key: 'saleDate',
      label: 'Date',
      sortable: true,
      width: '160px',
      formatter: (val) => {
        if (!val) return '-';
        const d = new Date(val as string);
        return isNaN(d.getTime()) ? '-' : d.toLocaleDateString('en-IN');
      }
    },
    { key: 'customerName', label: 'Customer', sortable: true },
    { key: 'invoiceType', label: 'Type', sortable: false, width: '120px' },
    { key: 'paymentMode', label: 'Mode', sortable: false, width: '100px' },
    { key: 'status', label: 'Status', sortable: false, width: '110px' },
    {
      key: 'grandTotal',
      label: 'Grand Total',
      sortable: true,
      align: 'right',
      width: '130px',
      formatter: (val) => (val !== undefined && val !== null ? `₹${Number(val).toFixed(2)}` : '₹0.00')
    },
    {
      key: 'amountPaid',
      label: 'Paid',
      sortable: false,
      align: 'right',
      width: '130px',
      formatter: (val) => (val !== undefined && val !== null ? `₹${Number(val).toFixed(2)}` : '₹0.00')
    },
    {
      key: 'balance',
      label: 'Balance',
      sortable: true,
      align: 'right',
      width: '130px',
      formatter: (val) => (val !== undefined && val !== null ? `₹${Number(val).toFixed(2)}` : '₹0.00')
    }
  ];

  ngOnInit(): void {
    this.querySub = this.route.queryParams.subscribe((params) => {
      this.syncStateFromParams(params);
      this.fetchSales();
    });
  }

  ngOnDestroy(): void {
    this.querySub?.unsubscribe();
  }

  syncStateFromParams(params: Params): void {
    this.search.set(params['search'] || '');
    this.dateFilter.set(params['dateFilter'] || 'all');
    this.customFrom.set(params['customFrom'] || '');
    this.customTo.set(params['customTo'] || '');
    this.status.set(params['status'] || '');
    this.paymentMode.set(params['paymentMode'] || '');
    this.invoiceType.set(params['invoiceType'] || '');
    this.paidOnly.set(params['paidOnly'] === 'true');
    this.dueOnly.set(params['dueOnly'] === 'true');
    this.financedOnly.set(params['financedOnly'] === 'true');
    this.category.set(params['category'] || '');
    this.minAmount.set(params['minAmount'] ? Number(params['minAmount']) : null);
    this.maxAmount.set(params['maxAmount'] ? Number(params['maxAmount']) : null);

    this.sortBy.set(params['sortBy'] || 'SaleDate');
    this.sortDirection.set(params['sortDirection'] === 'asc' ? 'asc' : 'desc');
    this.page.set(params['page'] ? Number(params['page']) : 1);
    this.pageSize.set(params['pageSize'] ? Number(params['pageSize']) : 50);
  }

  fetchSales(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    const fromDate = this.customFrom() ? new Date(this.customFrom()) : undefined;
    const toDate = this.customTo() ? new Date(this.customTo()) : undefined;

    this.salesClient
      .getSales(
        this.search() || undefined,
        this.dateFilter() !== 'all' ? this.dateFilter() : undefined,
        fromDate,
        toDate,
        this.status() || undefined,
        this.paymentMode() || undefined,
        this.invoiceType() || undefined,
        this.paidOnly() ? true : undefined,
        this.dueOnly() ? true : undefined,
        this.financedOnly() ? true : undefined,
        this.category() || undefined,
        this.minAmount() !== null ? this.minAmount()! : undefined,
        this.maxAmount() !== null ? this.maxAmount()! : undefined,
        this.sortBy(),
        this.sortDirection(),
        this.page(),
        this.pageSize()
      )
      .subscribe({
        next: (result: PagedResultOfSaleSummaryDto) => {
          this.sales.set(result.items || []);
          this.totalCount.set(result.totalCount || 0);
          this.isLoading.set(false);
        },
        error: (err) => {
          this.isLoading.set(false);
          this.errorMessage.set('Failed to load sales history.');
        }
      });
  }

  updateParam(key: keyof SalesFilterState, value: unknown): void {
    const nextParams: Params = { ...this.route.snapshot.queryParams };

    if (key !== 'page') {
      nextParams['page'] = 1;
    }

    if (value === null || value === undefined || value === '' || value === false || (key === 'dateFilter' && value === 'all')) {
      delete nextParams[key as string];
    } else {
      nextParams[key as string] = String(value);
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

  onRowClick(row: SaleSummaryDto): void {
    if (!row.id) return;
    if (row.invoiceType?.toLowerCase() === 'estimate') {
      this.router.navigate(['/sales/estimates', row.id]);
    } else {
      this.router.navigate(['/sales', row.id]);
    }
  }

  resetFilters(): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {}
    });
  }
}
