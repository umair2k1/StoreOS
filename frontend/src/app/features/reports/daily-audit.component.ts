import { Component, inject, signal, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { ReportsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe } from '../../shared';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

export interface SalesSummary {
  revenue: number;
  buyingCost: number;
  profit: number;
  gstCollected: number;
  invoiceCount: number;
  itemsSold: number;
}

export interface InventorySummary {
  totalStockVolume: number;
  stockWorthAtCost: number;
  retailValuation: number;
  lowStockCount: number;
  outOfStockCount: number;
}

export interface PaymentBreakdown {
  cash: number;
  upi: number;
  finance: number;
  other: number;
  total: number;
}

export interface CategorySale {
  category: string;
  revenue: number;
  itemsSold: number;
}

export interface ItemizedSaleLine {
  invoiceNumber: string;
  date: string;
  customerName: string;
  itemName: string;
  qty: number;
  sellingValue: number;
  costValue: number;
  gst: number;
  imei?: string | null;
  paymentMode?: string | null;
}

export interface DailySalesReportData {
  date: string;
  sales: SalesSummary;
  inventory: InventorySummary;
  payments: PaymentBreakdown;
  byCategory: CategorySale[];
  itemizedLog: ItemizedSaleLine[];
  collectedAmount: number;
  outstandingAmount: number;
}

@Component({
  selector: 'app-daily-audit',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PzMoneyAliasPipe],
  templateUrl: './daily-audit.component.html',
  styleUrl: './daily-audit.component.scss'
})
export class DailyAuditComponent implements OnInit {
  private readonly reportsClient = inject(ReportsClient);
  private readonly http = inject(HttpClient);
  private readonly toastService = inject(ToastService);
  private readonly brandingStore = inject(BrandingSignalStore);

  readonly shopName = this.brandingStore.tenantName;
  readonly branchName = this.brandingStore.activeBranchName;
  readonly logoUrl = this.brandingStore.logoUrl;

  readonly selectedDate = signal<string>(this.computeToday());
  readonly report = signal<DailySalesReportData | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly isPrinting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly searchQuery = signal<string>('');

  readonly filteredLog = computed(() => {
    const r = this.report();
    if (!r) return [];
    const query = this.searchQuery().trim().toLowerCase();
    if (!query) return r.itemizedLog;

    return r.itemizedLog.filter(item => {
      const matchInv = item.invoiceNumber.toLowerCase().includes(query);
      const matchCust = item.customerName.toLowerCase().includes(query);
      const matchItem = item.itemName.toLowerCase().includes(query);
      const matchImei = item.imei?.toLowerCase().includes(query);
      return matchInv || matchCust || matchItem || matchImei;
    });
  });

  getProfitMarginPct(r: DailySalesReportData): number {
    if (!r.sales.revenue || r.sales.revenue <= 0) return 0;
    return Math.round((r.sales.profit / r.sales.revenue) * 1000) / 10;
  }

  ngOnInit(): void {
    this.loadReport();
  }

  computeToday(): string {
    const d = new Date();
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  async loadReport(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const dateVal = new Date(this.selectedDate());
      const resp = await firstValueFrom(this.reportsClient.getDailySales(dateVal, null));
      const parsed = await this.parseFileResponse<DailySalesReportData>(resp);
      this.report.set(parsed);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load daily sales report.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  async printReport(): Promise<void> {
    this.isPrinting.set(true);
    try {
      const date = this.selectedDate();
      const blob = await firstValueFrom(
        this.http.get(`/api/reports/daily-sales.pdf?date=${date}`, { responseType: 'blob' })
      );
      const url = window.URL.createObjectURL(blob);
      const win = window.open(url, '_blank');
      if (!win) {
        // Fallback download if popup blocked
        const a = document.createElement('a');
        a.href = url;
        a.download = `daily-sales-${date}.pdf`;
        a.click();
      }
      this.toastService.success('Daily sales report PDF opened for printing.');
    } catch {
      // Graceful fallback to browser print if PDF generation endpoint unavailable
      window.print();
    } finally {
      this.isPrinting.set(false);
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp || !resp.data) return null;
    if (typeof resp.data === 'string') {
      return JSON.parse(resp.data) as T;
    }
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      return JSON.parse(text) as T;
    }
    return resp.data as T;
  }
}
