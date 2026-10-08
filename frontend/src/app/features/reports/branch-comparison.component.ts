import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ReportsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe } from '../../shared';

export interface BranchComparisonRow {
  branchId: string;
  branchName: string;
  revenue: number;
  invoiceCount: number;
  profit: number;
  activeRepairs: number;
}

export interface BranchComparisonData {
  branches: BranchComparisonRow[];
  consolidated: BranchComparisonRow;
}

@Component({
  selector: 'app-branch-comparison',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PzMoneyAliasPipe],
  templateUrl: './branch-comparison.component.html',
  styleUrl: './branch-comparison.component.scss'
})
export class BranchComparisonComponent implements OnInit {
  private readonly reportsClient = inject(ReportsClient);
  private readonly auth = inject(AuthSignalStore);
  private readonly toastService = inject(ToastService);

  // HARD CONSTRAINT: Visible ONLY for multi-branch/all-branches users (Plan Section 12.3)
  readonly isMultiBranch = computed(
    () => this.auth.allBranches() || this.auth.branchIds().length > 1
  );

  readonly selectedDate = signal<string>(this.computeToday());
  readonly comparisonData = signal<BranchComparisonData | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    if (this.isMultiBranch()) {
      this.loadComparison();
    }
  }

  computeToday(): string {
    const d = new Date();
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  async loadComparison(): Promise<void> {
    if (!this.isMultiBranch()) return;

    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const dateVal = new Date(this.selectedDate());
      const resp = await firstValueFrom(this.reportsClient.getBranchComparison(dateVal));
      const parsed = await this.parseFileResponse<BranchComparisonData>(resp);
      this.comparisonData.set(parsed);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load branch comparison.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
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
