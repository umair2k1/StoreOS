import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  CdkDragDrop,
  DragDropModule,
  moveItemInArray,
  transferArrayItem
} from '@angular/cdk/drag-drop';
import {
  RepairJobsClient,
  SettingsClient
} from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe } from '../../shared';

export interface RepairJobItem {
  id: string;
  jobNumber: string;
  customerName?: string | null;
  customerPhone?: string | null;
  deviceBrand?: string | null;
  deviceModel?: string | null;
  deviceType?: string | null;
  problemDescription?: string | null;
  estimatedCost?: number | null;
  finalTotal?: number | null;
  status: string;
  expectedCompletion?: Date | string | null;
  createdAt?: Date | string | null;
}

interface PagedRepairJobsResult {
  items?: RepairJobItem[];
  totalCount?: number;
  page?: number;
  pageSize?: number;
}

export const ACTIVE_COLUMNS: readonly string[] = [
  'Received',
  'Diagnosing',
  'WaitingForApproval',
  'WaitingForParts',
  'InRepair',
  'ReadyForPickup'
];

@Component({
  selector: 'app-job-board',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    DragDropModule,
    PzMoneyAliasPipe
  ],
  templateUrl: './job-board.component.html',
  styleUrl: './job-board.component.scss'
})
export class JobBoardComponent implements OnInit {
  private readonly repairJobsClient = inject(RepairJobsClient);
  private readonly settingsClient = inject(SettingsClient);
  private readonly toastService = inject(ToastService);

  readonly activeColumns = ACTIVE_COLUMNS;
  readonly isRepairsEnabled = signal<boolean>(true);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Grouped column lists for CdkDropList
  readonly columnsMap = signal<Record<string, RepairJobItem[]>>({
    Received: [],
    Diagnosing: [],
    WaitingForApproval: [],
    WaitingForParts: [],
    InRepair: [],
    ReadyForPickup: []
  });

  // Closed jobs (Delivered / Cancelled)
  readonly closedJobs = signal<RepairJobItem[]>([]);
  readonly isClosedSectionExpanded = signal<boolean>(false);

  ngOnInit(): void {
    this.initComponent();
  }

  async initComponent(): Promise<void> {
    const enabled = await this.checkRepairsEnabled();
    this.isRepairsEnabled.set(enabled);
    if (enabled) {
      await this.loadJobs();
    }
  }

  async checkRepairsEnabled(): Promise<boolean> {
    try {
      const resp = await firstValueFrom(this.settingsClient.getRepairsEnabled());
      const parsed = await this.parseFileResponse<{ enabled?: boolean } | boolean>(resp);
      if (typeof parsed === 'boolean') return parsed;
      if (parsed && typeof parsed === 'object' && 'enabled' in parsed) {
        return parsed.enabled ?? true;
      }
      return true;
    } catch {
      return false;
    }
  }

  async loadJobs(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const resp = await firstValueFrom(this.repairJobsClient.list(null, null, 1, 100));
      const parsed = await this.parseFileResponse<PagedRepairJobsResult | RepairJobItem[]>(resp);
      const allJobs: RepairJobItem[] = Array.isArray(parsed)
        ? parsed
        : parsed?.items ?? [];

      const newMap: Record<string, RepairJobItem[]> = {
        Received: [],
        Diagnosing: [],
        WaitingForApproval: [],
        WaitingForParts: [],
        InRepair: [],
        ReadyForPickup: []
      };
      const closed: RepairJobItem[] = [];

      for (const job of allJobs) {
        if (job.status === 'Delivered' || job.status === 'Cancelled') {
          closed.push(job);
        } else if (newMap[job.status]) {
          newMap[job.status].push(job);
        } else {
          // Fallback unknown active status into Received
          newMap['Received'].push(job);
        }
      }

      this.columnsMap.set(newMap);
      this.closedJobs.set(closed);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load repair jobs.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  toggleClosedSection(): void {
    this.isClosedSectionExpanded.update(val => !val);
  }

  async onDrop(event: CdkDragDrop<RepairJobItem[]>, targetStatus: string): Promise<void> {
    if (event.previousContainer === event.container) {
      moveItemInArray(event.container.data, event.previousIndex, event.currentIndex);
      this.columnsMap.update(m => ({ ...m }));
      return;
    }

    const job = event.previousContainer.data[event.previousIndex];
    if (!job) return;
    const originalStatus = job.status;

    // Tentatively transfer card to the new column
    transferArrayItem(
      event.previousContainer.data,
      event.container.data,
      event.previousIndex,
      event.currentIndex
    );
    job.status = targetStatus;
    this.columnsMap.update(m => ({ ...m }));

    try {
      await firstValueFrom(
        this.repairJobsClient.changeStatus(job.id, { status: targetStatus })
      );
      this.toastService.success(`Job ${job.jobNumber} moved to ${targetStatus}.`);
    } catch (err: unknown) {
      // REVERT HARD CONSTRAINT: visually revert the card to its original column and show error toast
      const currIdx = event.container.data.indexOf(job);
      if (currIdx !== -1) {
        event.container.data.splice(currIdx, 1);
        event.previousContainer.data.splice(event.previousIndex, 0, job);
      }
      job.status = originalStatus;
      this.columnsMap.update(m => ({ ...m }));

      const msg = err instanceof Error ? err.message : `Illegal transition from ${originalStatus} to ${targetStatus}.`;
      this.toastService.error(msg);
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
