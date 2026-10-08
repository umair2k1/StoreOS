import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { TransfersClient } from '../../core/api/generated-client';
import { BranchContextService } from '../../core/branch/branch-context.service';
import { ToastService } from '../../shared/services/toast.service';

export interface PendingTransferItem {
  id: string;
  fromBranchId: string;
  toBranchId: string;
  productId: string;
  productName?: string | null;
  qty: number;
  status: string;
  imeis?: string[] | null;
  createdAt?: string | Date;
}

@Component({
  selector: 'app-receive-transfer',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './receive-transfer.component.html',
  styleUrl: './receive-transfer.component.scss'
})
export class ReceiveTransferComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly transfersClient = inject(TransfersClient);
  private readonly branchContext = inject(BranchContextService);
  private readonly toastService = inject(ToastService);

  readonly allTransfers = signal<PendingTransferItem[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly receivingTransferId = signal<string | null>(null);

  // HARD CONSTRAINT: The pending-transfers list must ONLY show transfers
  // addressed to the CURRENT branch context (toBranchId === currentBranchId),
  // NEVER transfers the branch is merely the SOURCE of.
  readonly pendingTransfers = computed<PendingTransferItem[]>(() => {
    const currentBranchId = this.branchContext.currentBranchId();
    if (!currentBranchId) {
      return [];
    }
    return this.allTransfers().filter(t =>
      t.toBranchId === currentBranchId &&
      t.status.toLowerCase() === 'pending'
    );
  });

  ngOnInit(): void {
    this.loadPendingTransfers();
  }

  async loadPendingTransfers(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const data = await firstValueFrom(this.http.get<PendingTransferItem[]>('/api/inventory/transfers/pending'));
      this.allTransfers.set(data || []);
    } catch {
      this.errorMessage.set('Failed to load pending transfers.');
    } finally {
      this.isLoading.set(false);
    }
  }

  setTransfers(items: PendingTransferItem[]): void {
    this.isLoading.set(false);
    this.allTransfers.set(items);
  }

  async receiveTransfer(transfer: PendingTransferItem): Promise<void> {
    if (this.receivingTransferId()) {
      return;
    }

    this.receivingTransferId.set(transfer.id);
    try {
      await firstValueFrom(this.transfersClient.receive(transfer.id));
      // Remove from pending list on success
      this.allTransfers.update(current => current.filter(t => t.id !== transfer.id));
      this.toastService.success(`Transfer for "${transfer.productName || transfer.productId}" received.`);
    } catch {
      this.toastService.error('Failed to confirm receipt of transfer.');
    } finally {
      this.receivingTransferId.set(null);
    }
  }
}
