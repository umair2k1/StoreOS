import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BranchesClient } from '../../core/api/generated-client';
import type {
  CreateBranchCommand,
  UpdateBranchBody,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

export interface BranchItem {
  id: string;
  code: string;
  name: string;
  address?: string | null;
  phone?: string | null;
  email?: string | null;
  gstin?: string | null;
  isActive: boolean;
}

@Component({
  selector: 'app-branches-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './branches-admin.component.html',
  styleUrl: './branches-admin.component.scss'
})
export class BranchesAdminComponent implements OnInit {
  private readonly branchesClient = inject(BranchesClient);
  private readonly toastService = inject(ToastService);

  readonly branches = signal<BranchItem[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Modal State
  readonly isModalOpen = signal<boolean>(false);
  readonly isSaving = signal<boolean>(false);
  readonly editingBranchId = signal<string | null>(null);

  // Form Fields
  readonly formCode = signal<string>('');
  readonly formName = signal<string>('');
  readonly formAddress = signal<string>('');
  readonly formPhone = signal<string>('');
  readonly formEmail = signal<string>('');
  readonly formGstin = signal<string>('');
  readonly formIsActive = signal<boolean>(true);

  ngOnInit(): void {
    this.loadBranches();
  }

  async loadBranches(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const resp = await firstValueFrom(this.branchesClient.list());
      const parsed = await this.parseFileResponse<BranchItem[]>(resp);
      this.branches.set(Array.isArray(parsed) ? parsed : []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load branches.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  openCreateModal(): void {
    this.editingBranchId.set(null);
    this.formCode.set('');
    this.formName.set('');
    this.formAddress.set('');
    this.formPhone.set('');
    this.formEmail.set('');
    this.formGstin.set('');
    this.formIsActive.set(true);
    this.isModalOpen.set(true);
  }

  openEditModal(branch: BranchItem): void {
    this.editingBranchId.set(branch.id);
    this.formCode.set(branch.code);
    this.formName.set(branch.name);
    this.formAddress.set(branch.address || '');
    this.formPhone.set(branch.phone || '');
    this.formEmail.set(branch.email || '');
    this.formGstin.set(branch.gstin || '');
    this.formIsActive.set(branch.isActive);
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
    this.editingBranchId.set(null);
  }

  async saveBranch(): Promise<void> {
    const code = this.formCode().trim().toUpperCase();
    const name = this.formName().trim();

    if (!this.editingBranchId() && !code) {
      this.toastService.error('Branch code is required.');
      return;
    }
    if (!name) {
      this.toastService.error('Branch name is required.');
      return;
    }

    this.isSaving.set(true);
    try {
      const editId = this.editingBranchId();
      if (editId) {
        const body: UpdateBranchBody = {
          name,
          address: this.formAddress().trim() || undefined,
          phone: this.formPhone().trim() || undefined,
          email: this.formEmail().trim() || undefined,
          gstin: this.formGstin().trim() || undefined,
          isActive: this.formIsActive()
        };
        await firstValueFrom(this.branchesClient.update(editId, body));
        this.toastService.success(`Branch '${name}' updated.`);
      } else {
        const command: CreateBranchCommand = {
          code,
          name,
          address: this.formAddress().trim() || undefined,
          phone: this.formPhone().trim() || undefined,
          email: this.formEmail().trim() || undefined,
          gstin: this.formGstin().trim() || undefined
        };
        await firstValueFrom(this.branchesClient.create(command));
        this.toastService.success(`Branch '${name}' created.`);
      }
      this.closeModal();
      await this.loadBranches();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to save branch.';
      this.toastService.error(msg);
    } finally {
      this.isSaving.set(false);
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
