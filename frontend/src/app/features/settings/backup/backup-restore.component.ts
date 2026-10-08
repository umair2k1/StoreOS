import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PlatformClient } from '../../../core/api/generated-client';
import type {
  TenantBackupDto,
  FileResponse
} from '../../../core/api/generated-client';
import { BrandingSignalStore } from '../../../core/branding/branding-signal-store';
import { ToastService } from '../../../shared/services/toast.service';
import { PermissionDirective } from '../../../core/directives/permission.directive';

export interface ImportResult {
  success: boolean;
  recordCounts?: Record<string, number>;
  errorMessage?: string | null;
}

@Component({
  selector: 'app-backup-restore',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PermissionDirective],
  templateUrl: './backup-restore.component.html',
  styleUrl: './backup-restore.component.scss'
})
export class BackupRestoreComponent {
  private readonly platformClient = inject(PlatformClient);
  private readonly brandingStore = inject(BrandingSignalStore);
  private readonly toastService = inject(ToastService);

  readonly isExporting = signal<boolean>(false);
  readonly isImporting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly importSuccessResult = signal<ImportResult | null>(null);

  // File selection & Confirmation Dialog State
  readonly isConfirmDialogOpen = signal<boolean>(false);
  readonly selectedBackup = signal<TenantBackupDto | null>(null);
  readonly selectedFileName = signal<string>('');
  readonly confirmationInput = signal<string>('');

  // HARD CONSTRAINT: The expected phrase the user must type to confirm
  readonly expectedConfirmationPhrase = computed(
    () => this.brandingStore.tenantName() || 'STORE-OVERWRITE-CONFIRM'
  );

  readonly isConfirmationMatching = computed(
    () => this.confirmationInput().trim() === this.expectedConfirmationPhrase().trim()
  );

  async exportBackup(): Promise<void> {
    this.isExporting.set(true);
    this.errorMessage.set(null);
    try {
      const backup = await firstValueFrom(this.platformClient.export());
      const jsonStr = JSON.stringify(backup, null, 2);
      const blob = new Blob([jsonStr], { type: 'application/json' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      const today = new Date().toISOString().slice(0, 10);
      a.href = url;
      a.download = `tenant-backup-${today}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      window.URL.revokeObjectURL(url);
      this.toastService.success('Tenant backup exported and downloaded.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to export backup.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isExporting.set(false);
    }
  }

  async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    if (!input.files || input.files.length === 0) return;

    const file = input.files[0];
    this.selectedFileName.set(file.name);
    this.errorMessage.set(null);
    this.importSuccessResult.set(null);

    try {
      const text = await file.text();
      const parsed = JSON.parse(text) as TenantBackupDto;
      this.selectedBackup.set(parsed);
      this.confirmationInput.set('');
      this.isConfirmDialogOpen.set(true);
    } catch {
      this.errorMessage.set('Invalid JSON backup file. Please select a valid StoreOS backup export.');
      this.toastService.error('Invalid backup file.');
    } finally {
      input.value = '';
    }
  }

  closeConfirmDialog(): void {
    this.isConfirmDialogOpen.set(false);
    this.selectedBackup.set(null);
    this.confirmationInput.set('');
  }

  // HARD CONSTRAINT: Import must be gated behind explicit type-to-confirm step
  async executeRestore(): Promise<void> {
    if (!this.isConfirmationMatching()) {
      this.toastService.error('Confirmation phrase does not match.');
      return;
    }

    const backup = this.selectedBackup();
    if (!backup) return;

    this.isImporting.set(true);
    this.errorMessage.set(null);
    try {
      const resp = await firstValueFrom(this.platformClient.import(backup));
      const result = await this.parseFileResponse<ImportResult>(resp);

      if (result && result.success) {
        this.importSuccessResult.set(result);
        this.toastService.success('Tenant data successfully restored from backup.');
        this.closeConfirmDialog();
      } else {
        const errorDetail = result?.errorMessage || 'Restore operation reported failure.';
        this.errorMessage.set(errorDetail);
        this.toastService.error(errorDetail);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Schema version mismatch or failed import.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isImporting.set(false);
    }
  }

  getRecordCountEntries(counts?: Record<string, number>): Array<{ table: string; count: number }> {
    if (!counts) return [];
    return Object.entries(counts).map(([table, count]) => ({ table, count }));
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
