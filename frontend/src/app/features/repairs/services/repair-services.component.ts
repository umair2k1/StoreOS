import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  RepairServicesClient,
  SettingsClient
} from '../../../core/api/generated-client';
import type {
  CreateRepairServiceCommand,
  UpdateRepairServiceBody,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import { PzMoneyAliasPipe } from '../../../shared';

export interface RepairServiceItem {
  id: string;
  name: string;
  description?: string | null;
  standardCharge: number;
  taxable: boolean;
  gstRate: number;
  sacCode?: string | null;
  estimatedDurationMinutes?: number | null;
  warrantyDays?: number | null;
  isActive: boolean;
}

interface PagedRepairServicesResult {
  items?: RepairServiceItem[];
  totalCount?: number;
  page?: number;
  pageSize?: number;
}

@Component({
  selector: 'app-repair-services',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PzMoneyAliasPipe],
  templateUrl: './repair-services.component.html',
  styleUrl: './repair-services.component.scss'
})
export class RepairServicesComponent implements OnInit {
  private readonly repairServicesClient = inject(RepairServicesClient);
  private readonly settingsClient = inject(SettingsClient);
  private readonly toastService = inject(ToastService);

  readonly isRepairsEnabled = signal<boolean>(true);
  readonly services = signal<RepairServiceItem[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Form modal state
  readonly isModalOpen = signal<boolean>(false);
  readonly isSaving = signal<boolean>(false);
  readonly editingServiceId = signal<string | null>(null);

  // Service form fields with defaults from P-701
  readonly formName = signal<string>('');
  readonly formDescription = signal<string>('');
  readonly formStandardCharge = signal<number>(0);
  readonly formTaxable = signal<boolean>(true);
  readonly formGstRate = signal<number>(18); // Default 18% GST (P-701)
  readonly formSacCode = signal<string>('998713'); // Default SAC code (P-701)
  readonly formEstimatedDuration = signal<number>(60); // Default 60 min (P-701)
  readonly formWarrantyDays = signal<number>(30); // Default 30 days (P-701)

  ngOnInit(): void {
    this.initComponent();
  }

  async initComponent(): Promise<void> {
    const enabled = await this.checkRepairsEnabled();
    this.isRepairsEnabled.set(enabled);
    if (enabled) {
      await this.loadServices();
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

  async loadServices(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const resp = await firstValueFrom(this.repairServicesClient.list(null, 1, 100));
      const parsed = await this.parseFileResponse<PagedRepairServicesResult | RepairServiceItem[]>(resp);
      if (parsed) {
        if (Array.isArray(parsed)) {
          this.services.set(parsed);
        } else if (parsed.items) {
          this.services.set(parsed.items);
        } else {
          this.services.set([]);
        }
      } else {
        this.services.set([]);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load repair services.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  openCreateModal(): void {
    this.editingServiceId.set(null);
    this.formName.set('');
    this.formDescription.set('');
    this.formStandardCharge.set(0);
    this.formTaxable.set(true);
    // Explicitly apply SAC/GST/duration/warranty defaults from P-701
    this.formGstRate.set(18);
    this.formSacCode.set('998713');
    this.formEstimatedDuration.set(60);
    this.formWarrantyDays.set(30);
    this.isModalOpen.set(true);
  }

  openEditModal(item: RepairServiceItem): void {
    this.editingServiceId.set(item.id);
    this.formName.set(item.name);
    this.formDescription.set(item.description || '');
    this.formStandardCharge.set(item.standardCharge);
    this.formTaxable.set(item.taxable);
    this.formGstRate.set(item.gstRate ?? 18);
    this.formSacCode.set(item.sacCode || '998713');
    this.formEstimatedDuration.set(item.estimatedDurationMinutes ?? 60);
    this.formWarrantyDays.set(item.warrantyDays ?? 30);
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
    this.editingServiceId.set(null);
  }

  async saveService(): Promise<void> {
    const name = this.formName().trim();
    if (!name) {
      this.toastService.error('Service name is required.');
      return;
    }
    if (this.formStandardCharge() < 0) {
      this.toastService.error('Standard charge cannot be negative.');
      return;
    }

    this.isSaving.set(true);
    try {
      const editId = this.editingServiceId();
      if (editId) {
        const body: UpdateRepairServiceBody = {
          name,
          description: this.formDescription().trim() || undefined,
          standardCharge: this.formStandardCharge(),
          taxable: this.formTaxable(),
          gstRate: this.formTaxable() ? this.formGstRate() : 0,
          sacCode: this.formSacCode().trim() || undefined,
          estimatedDurationMinutes: this.formEstimatedDuration(),
          warrantyDays: this.formWarrantyDays()
        };
        await firstValueFrom(this.repairServicesClient.update(editId, body));
        this.toastService.success('Repair service updated.');
      } else {
        const command: CreateRepairServiceCommand = {
          name,
          description: this.formDescription().trim() || undefined,
          standardCharge: this.formStandardCharge(),
          taxable: this.formTaxable(),
          gstRate: this.formTaxable() ? this.formGstRate() : 0,
          sacCode: this.formSacCode().trim() || undefined,
          estimatedDurationMinutes: this.formEstimatedDuration(),
          warrantyDays: this.formWarrantyDays()
        };
        await firstValueFrom(this.repairServicesClient.create(command));
        this.toastService.success('Repair service created.');
      }
      this.closeModal();
      await this.loadServices();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to save repair service.';
      this.toastService.error(msg);
    } finally {
      this.isSaving.set(false);
    }
  }

  async toggleDeactivate(item: RepairServiceItem): Promise<void> {
    try {
      if (item.isActive) {
        await firstValueFrom(this.repairServicesClient.deactivate(item.id));
        this.toastService.success(`Deactivated ${item.name}.`);
      } else {
        await firstValueFrom(this.repairServicesClient.update(item.id, { isActive: true }));
        this.toastService.success(`Reactivated ${item.name}.`);
      }
      await this.loadServices();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Operation failed.';
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
