import type { ElementRef } from '@angular/core';
import { Component, inject, signal, ViewChild, OnInit, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { Chart } from 'chart.js/auto';
import {
  ReportsClient,
  SettingsClient
} from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe } from '../../shared';

export interface LowStockItem {
  productId: string;
  name: string;
  stockQty: number;
  threshold: number;
}

export interface TopSellingItem {
  itemName: string;
  totalQtySold: number;
}

export interface DailyRevenuePoint {
  date: string;
  revenue: number;
}

export interface DashboardData {
  todayRevenue: number;
  todayInvoiceCount: number;
  monthlyProfit: number;
  activeRepairsCount: number;
  readyForPickupCount: number;
  lowStockItems: LowStockItem[];
  topSellingItems: TopSellingItem[];
  sevenDayRevenue: DailyRevenuePoint[];
}

@Component({
  selector: 'app-dashboard-widgets',
  standalone: true,
  imports: [CommonModule, RouterLink, PzMoneyAliasPipe],
  templateUrl: './dashboard-widgets.component.html',
  styleUrl: './dashboard-widgets.component.scss'
})
export class DashboardWidgetsComponent implements OnInit, AfterViewInit {
  private readonly reportsClient = inject(ReportsClient);
  private readonly settingsClient = inject(SettingsClient);
  private readonly toastService = inject(ToastService);

  @ViewChild('revenueChart') chartCanvas?: ElementRef<HTMLCanvasElement>;

  private chartInstance: Chart | null = null;

  readonly isRepairsEnabled = signal<boolean>(false);
  readonly dashboardData = signal<DashboardData | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.initData();
  }

  ngAfterViewInit(): void {
    if (this.dashboardData()?.sevenDayRevenue) {
      this.renderChart(this.dashboardData()!.sevenDayRevenue);
    }
  }

  async initData(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      await Promise.all([this.checkRepairsEnabled(), this.loadDashboard()]);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load dashboard.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  async checkRepairsEnabled(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.settingsClient.getRepairsEnabled());
      const parsed = await this.parseFileResponse<{ enabled?: boolean } | boolean>(resp);
      if (typeof parsed === 'boolean') {
        this.isRepairsEnabled.set(parsed);
      } else if (parsed && typeof parsed === 'object' && 'enabled' in parsed) {
        this.isRepairsEnabled.set(parsed.enabled ?? false);
      } else {
        this.isRepairsEnabled.set(false);
      }
    } catch {
      this.isRepairsEnabled.set(false);
    }
  }

  async loadDashboard(): Promise<void> {
    const resp = await firstValueFrom(this.reportsClient.getDashboard(null));
    const parsed = await this.parseFileResponse<DashboardData>(resp);
    if (parsed) {
      this.dashboardData.set(parsed);
      this.renderChart(parsed.sevenDayRevenue || []);
    }
  }

  renderChart(points: DailyRevenuePoint[]): void {
    if (!this.chartCanvas?.nativeElement) return;
    try {
      const ctx = this.chartCanvas.nativeElement.getContext('2d');
      if (!ctx) return;

      if (this.chartInstance) {
        this.chartInstance.destroy();
        this.chartInstance = null;
      }

      this.chartInstance = new Chart(ctx, {
        type: 'line',
        data: {
          labels: points.map(p => p.date),
          datasets: [
            {
              label: 'Daily Revenue (₹)',
              data: points.map(p => p.revenue),
              borderColor: '#2563eb',
              backgroundColor: 'rgba(37, 99, 235, 0.1)',
              fill: true,
              tension: 0.3
            }
          ]
        },
        options: {
          responsive: true,
          maintainAspectRatio: false
        }
      });
    } catch {
      // In non-canvas test environments, fail silently
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
