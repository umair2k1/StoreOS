import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LiabilitiesClient } from '../../core/api/generated-client';
import type {
  CreateLiabilityCommand,
  RecordRepaymentBody,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe, PzDateAliasPipe } from '../../shared';

export interface LiabilityItem {
  id: string;
  title: string;
  type: string;
  lenderName?: string | null;
  totalPrincipal: number;
  currentBalance: number;
  dueDate?: Date | string | null;
  status?: string | null;
  notes?: string | null;
}

interface PagedLiabilitiesResult {
  items?: LiabilityItem[];
  totalCount?: number;
  page?: number;
  pageSize?: number;
}

@Component({
  selector: 'app-liabilities',
  standalone: true,
  imports: [CommonModule, FormsModule, PzMoneyAliasPipe, PzDateAliasPipe],
  templateUrl: './liabilities.component.html',
  styleUrl: './liabilities.component.scss'
})
export class LiabilitiesComponent implements OnInit {
  private readonly liabilitiesClient = inject(LiabilitiesClient);
  private readonly toastService = inject(ToastService);

  readonly liabilities = signal<LiabilityItem[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Create Liability modal state
  readonly isCreateModalOpen = signal<boolean>(false);
  readonly isCreating = signal<boolean>(false);
  readonly newTitle = signal<string>('');
  readonly newType = signal<string>('Loan');
  readonly newLenderName = signal<string>('');
  readonly newTotalPrincipal = signal<number>(0);
  readonly newDueDate = signal<string>('');
  readonly newNotes = signal<string>('');

  // Record Repayment modal state
  readonly isRepaymentModalOpen = signal<boolean>(false);
  readonly isSubmittingRepayment = signal<boolean>(false);
  readonly selectedLiability = signal<LiabilityItem | null>(null);
  readonly repaymentAmount = signal<number>(0);
  readonly repaymentMode = signal<string>('Cash');
  readonly repaymentDate = signal<string>(this.computeToday());
  readonly repaymentNotes = signal<string>('');

  readonly paymentModes: readonly string[] = ['Cash', 'UPI', 'Bank Transfer', 'Card', 'Cheque'];
  readonly liabilityTypes: readonly string[] = ['Loan', 'Credit Line', 'Supplier Credit', 'Promissory Note', 'Other'];

  ngOnInit(): void {
    this.loadLiabilities();
  }

  computeToday(): string {
    const d = new Date();
    const yyyy = d.getFullYear();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  async loadLiabilities(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const resp = await firstValueFrom(this.liabilitiesClient.list(1, 100));
      const parsed = await this.parseFileResponse<PagedLiabilitiesResult | LiabilityItem[]>(resp);
      if (parsed) {
        if (Array.isArray(parsed)) {
          this.liabilities.set(parsed);
        } else if (parsed.items) {
          this.liabilities.set(parsed.items);
        } else {
          this.liabilities.set([]);
        }
      } else {
        this.liabilities.set([]);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load liabilities.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  openCreateModal(): void {
    this.newTitle.set('');
    this.newType.set('Loan');
    this.newLenderName.set('');
    this.newTotalPrincipal.set(0);
    this.newDueDate.set('');
    this.newNotes.set('');
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  async submitCreateLiability(): Promise<void> {
    const title = this.newTitle().trim();
    const type = this.newType().trim();
    const principal = this.newTotalPrincipal();

    if (!title) {
      this.toastService.error('Title is required.');
      return;
    }
    if (!type) {
      this.toastService.error('Type is required.');
      return;
    }
    if (principal <= 0) {
      this.toastService.error('Total principal must be greater than zero.');
      return;
    }

    this.isCreating.set(true);
    try {
      const command: CreateLiabilityCommand = {
        title,
        type,
        lenderName: this.newLenderName().trim() || undefined,
        totalPrincipal: principal,
        dueDate: this.newDueDate() ? new Date(this.newDueDate()) : undefined,
        notes: this.newNotes().trim() || undefined
      };

      await firstValueFrom(this.liabilitiesClient.create(command));
      this.toastService.success('Liability created successfully.');
      this.closeCreateModal();
      await this.loadLiabilities();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create liability.';
      this.toastService.error(msg);
    } finally {
      this.isCreating.set(false);
    }
  }

  openRepaymentModal(liability: LiabilityItem): void {
    this.selectedLiability.set(liability);
    this.repaymentAmount.set(0);
    this.repaymentMode.set('Cash');
    this.repaymentDate.set(this.computeToday());
    this.repaymentNotes.set('');
    this.isRepaymentModalOpen.set(true);
  }

  closeRepaymentModal(): void {
    this.isRepaymentModalOpen.set(false);
    this.selectedLiability.set(null);
  }

  async submitRepayment(): Promise<void> {
    const liability = this.selectedLiability();
    if (!liability) return;

    const amount = this.repaymentAmount();
    const mode = this.repaymentMode().trim();
    const dateStr = this.repaymentDate();

    if (amount <= 0) {
      this.toastService.error('Repayment amount must be greater than zero.');
      return;
    }
    if (!mode) {
      this.toastService.error('Payment mode is required.');
      return;
    }
    if (!dateStr) {
      this.toastService.error('Payment date is required.');
      return;
    }

    this.isSubmittingRepayment.set(true);
    try {
      const body: RecordRepaymentBody = {
        amount,
        paymentMode: mode,
        paymentDate: new Date(dateStr),
        notes: this.repaymentNotes().trim() || undefined
      };

      await firstValueFrom(this.liabilitiesClient.recordRepayment(liability.id, body));
      this.toastService.success('Repayment recorded successfully.');

      // Update current balance in place and reload
      const updatedBalance = Math.max(0, liability.currentBalance - amount);
      this.liabilities.update(items =>
        items.map(item =>
          item.id === liability.id ? { ...item, currentBalance: updatedBalance } : item
        )
      );

      this.closeRepaymentModal();
      await this.loadLiabilities();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to record repayment.';
      this.toastService.error(msg);
    } finally {
      this.isSubmittingRepayment.set(false);
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
