import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { TransfersClient } from '../../core/api/generated-client';
import type { TransferOutCommand } from '../../core/api/generated-client';
import { BranchContextService } from '../../core/branch/branch-context.service';
import type { BranchInfo } from '../../core/branch/branch-context.service';
import { ToastService } from '../../shared/services/toast.service';

@Component({
  selector: 'app-transfer-out',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './transfer-out.component.html',
  styleUrl: './transfer-out.component.scss'
})
export class TransferOutComponent implements OnInit {
  private readonly transfersClient = inject(TransfersClient);
  private readonly branchContext = inject(BranchContextService);
  private readonly toastService = inject(ToastService);

  readonly toBranchId = signal<string>('');
  readonly productId = signal<string>('');
  readonly qty = signal<number>(1);
  readonly imeiList = signal<string[]>(['']);

  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  // Available destination branches: must exclude current branch
  readonly destinationBranches = computed<BranchInfo[]>(() => {
    const currentId = this.branchContext.currentBranchId();
    return this.branchContext.branches().filter(b => b.id !== currentId);
  });

  readonly isValid = computed<boolean>(() => {
    const dest = this.toBranchId().trim();
    const prod = this.productId().trim();
    const currentId = this.branchContext.currentBranchId();
    const validQty = this.qty() > 0;
    const destNotSelf = dest.length > 0 && dest !== currentId;
    return Boolean(destNotSelf && prod.length > 0 && validQty);
  });

  ngOnInit(): void {
    const dests = this.destinationBranches();
    if (dests.length > 0 && !this.toBranchId()) {
      this.toBranchId.set(dests[0].id);
    }
  }

  addImeiField(): void {
    this.imeiList.update(list => [...list, '']);
  }

  removeImeiField(index: number): void {
    if (this.imeiList().length <= 1) {
      this.imeiList.set(['']);
      return;
    }
    this.imeiList.update(list => list.filter((_, i) => i !== index));
  }

  updateImei(index: number, val: string): void {
    this.imeiList.update(list => {
      const copy = [...list];
      copy[index] = val;
      return copy;
    });
  }

  async submitTransfer(): Promise<void> {
    if (!this.isValid() || this.isSubmitting()) {
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);
    this.successMessage.set(null);

    const filteredImeis = this.imeiList()
      .map(i => i.trim())
      .filter(i => i.length > 0);

    const command: TransferOutCommand = {
      toBranchId: this.toBranchId(),
      productId: this.productId().trim(),
      qty: this.qty(),
      imeiStrings: filteredImeis.length > 0 ? filteredImeis : undefined
    };

    try {
      await firstValueFrom(this.transfersClient.transferOut(command));
      this.successMessage.set(`Successfully initiated transfer of ${this.qty()} unit(s).`);
      this.toastService.success('Branch transfer initiated successfully.');
      this.resetForm();
    } catch {
      this.errorMessage.set('Failed to initiate branch transfer. Please check stock and branch selection.');
      this.toastService.error('Failed to initiate transfer.');
    } finally {
      this.isSubmitting.set(false);
    }
  }

  resetForm(): void {
    this.productId.set('');
    this.qty.set(1);
    this.imeiList.set(['']);
  }
}
