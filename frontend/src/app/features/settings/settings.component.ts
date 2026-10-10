import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  SettingsClient,
  AuthClient
} from '../../core/api/generated-client';
import type {
  ChangePasswordCommand,
  FileResponse
} from '../../core/api/generated-client';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { ToastService } from '../../shared/services/toast.service';
import { LogoUploaderComponent } from '../onboarding/logo-uploader.component';
import { PermissionDirective } from '../../core/directives/permission.directive';

export type SettingsTab = 'shop' | 'tax' | 'bank' | 'finance' | 'theme' | 'repairs' | 'account';

export interface FinanceProviderItem {
  id?: string;
  displayName?: string;
  normalizedName?: string;
}

@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    LogoUploaderComponent,
    PermissionDirective
  ],
  templateUrl: './settings.component.html',
  styleUrl: './settings.component.scss'
})
export class SettingsComponent implements OnInit {
  private readonly settingsClient = inject(SettingsClient);
  private readonly authClient = inject(AuthClient);
  private readonly authStore = inject(AuthSignalStore);
  private readonly router = inject(Router);
  private readonly toastService = inject(ToastService);

  readonly activeTab = signal<SettingsTab>('shop');
  readonly isSaving = signal<boolean>(false);

  // 1. Shop Info
  readonly shopName = signal<string>('');
  readonly shopAddress = signal<string>('');
  readonly shopEmail = signal<string>('');
  readonly shopPhone = signal<string>('');
  readonly shopGstin = signal<string>('');
  readonly shopLogoUrl = signal<string>('');

  // 2. Tax Settings
  readonly defaultGstRate = signal<number>(18);

  // 3. Bank Details
  readonly bankName = signal<string>('');
  readonly accountName = signal<string>('');
  readonly accountNumber = signal<string>('');
  readonly ifsc = signal<string>('');
  readonly branch = signal<string>('');

  // 4. Finance Providers
  readonly financeProviders = signal<FinanceProviderItem[]>([]);
  readonly newProviderName = signal<string>('');

  // 5. Theme
  readonly currentTheme = signal<'light' | 'dark'>('light');

  // 6. Repairs Module Flag
  readonly repairsEnabled = signal<boolean>(false);

  // 7. My Account (Change Password)
  readonly currentPassword = signal<string>('');
  readonly newPassword = signal<string>('');
  readonly confirmPassword = signal<string>('');

  ngOnInit(): void {
    this.initThemeFromDom();
    this.loadSettings();
    this.loadFinanceProviders();
    this.loadRepairsEnabled();
  }

  setTab(tab: SettingsTab): void {
    this.activeTab.set(tab);
  }

  private initThemeFromDom(): void {
    const existing = document.documentElement.getAttribute('data-theme');
    if (existing === 'dark' || existing === 'light') {
      this.currentTheme.set(existing);
    } else {
      const stored = localStorage.getItem('storeos_theme');
      if (stored === 'dark' || stored === 'light') {
        this.currentTheme.set(stored);
        document.documentElement.setAttribute('data-theme', stored);
      }
    }
  }

  async loadFinanceProviders(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.settingsClient.getFinanceProviders());
      this.financeProviders.set(Array.isArray(resp) ? resp : []);
    } catch {
      // Non-critical, ignore
    }
  }

  async loadRepairsEnabled(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.settingsClient.getRepairsEnabled());
      const parsed = await this.parseFileResponse<{ enabled?: boolean } | boolean>(resp);
      if (typeof parsed === 'boolean') {
        this.repairsEnabled.set(parsed);
      } else if (parsed && typeof parsed === 'object' && 'enabled' in parsed) {
        this.repairsEnabled.set(parsed.enabled ?? false);
      }
    } catch {
      // Non-critical
    }
  }

  async loadSettings(): Promise<void> {
    await Promise.all([
      this.loadShopInfo(),
      this.loadTaxSettings(),
      this.loadBankDetails()
    ]);
  }

  async loadShopInfo(): Promise<void> {
    try {
      let resp: FileResponse;
      try {
        resp = await firstValueFrom(this.settingsClient.getSetting('shop_profile'));
      } catch {
        resp = await firstValueFrom(this.settingsClient.getSetting('shop_info'));
      }
      const data = await this.parseFileResponse<{
        name?: string;
        shopName?: string;
        address?: string;
        email?: string;
        phone?: string;
        gstin?: string;
        logoUrl?: string;
      }>(resp);
      if (data) {
        if (data.name !== undefined) this.shopName.set(data.name);
        else if (data.shopName !== undefined) this.shopName.set(data.shopName);
        if (data.address !== undefined) this.shopAddress.set(data.address);
        if (data.email !== undefined) this.shopEmail.set(data.email);
        if (data.phone !== undefined) this.shopPhone.set(data.phone);
        if (data.gstin !== undefined) this.shopGstin.set(data.gstin);
        if (data.logoUrl !== undefined) this.shopLogoUrl.set(data.logoUrl);
      }
    } catch {
      // Non-critical, keep default values
    }
  }

  async loadTaxSettings(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.settingsClient.getSetting('tax_settings'));
      const data = await this.parseFileResponse<{ defaultGstRate?: number }>(resp);
      if (data && data.defaultGstRate !== undefined) {
        this.defaultGstRate.set(data.defaultGstRate);
      }
    } catch {
      // Non-critical
    }
  }

  async loadBankDetails(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.settingsClient.getSetting('bank_details'));
      const data = await this.parseFileResponse<{
        bankName?: string;
        accountName?: string;
        accountNumber?: string;
        ifsc?: string;
        branch?: string;
      }>(resp);
      if (data) {
        if (data.bankName !== undefined) this.bankName.set(data.bankName);
        if (data.accountName !== undefined) this.accountName.set(data.accountName);
        if (data.accountNumber !== undefined) this.accountNumber.set(data.accountNumber);
        if (data.ifsc !== undefined) this.ifsc.set(data.ifsc);
        if (data.branch !== undefined) this.branch.set(data.branch);
      }
    } catch {
      // Non-critical
    }
  }

  onLogoUploaded(url: string): void {
    this.shopLogoUrl.set(url);
    this.toastService.success('Logo uploaded.');
  }

  async saveShopInfo(): Promise<void> {
    this.isSaving.set(true);
    try {
      const payload = {
        name: this.shopName().trim(),
        shopName: this.shopName().trim(),
        address: this.shopAddress().trim(),
        email: this.shopEmail().trim(),
        phone: this.shopPhone().trim(),
        gstin: this.shopGstin().trim(),
        logoUrl: this.shopLogoUrl()
      };
      await firstValueFrom(this.settingsClient.patchSetting('shop_profile', payload));
      this.toastService.success('Shop information saved.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to save shop info.';
      this.toastService.error(msg);
    } finally {
      this.isSaving.set(false);
    }
  }

  async saveTaxSettings(): Promise<void> {
    this.isSaving.set(true);
    try {
      const payload = {
        defaultGstRate: this.defaultGstRate()
      };
      await firstValueFrom(this.settingsClient.patchSetting('tax_settings', payload));
      this.toastService.success('Tax settings saved.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to save tax settings.';
      this.toastService.error(msg);
    } finally {
      this.isSaving.set(false);
    }
  }

  async saveBankDetails(): Promise<void> {
    this.isSaving.set(true);
    try {
      const payload = {
        bankName: this.bankName().trim(),
        accountName: this.accountName().trim(),
        accountNumber: this.accountNumber().trim(),
        ifsc: this.ifsc().trim(),
        branch: this.branch().trim()
      };
      await firstValueFrom(this.settingsClient.patchSetting('bank_details', payload));
      this.toastService.success('Bank details saved.');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to save bank details.';
      this.toastService.error(msg);
    } finally {
      this.isSaving.set(false);
    }
  }

  async addFinanceProvider(): Promise<void> {
    const name = this.newProviderName().trim();
    if (!name) return;

    try {
      await firstValueFrom(this.settingsClient.addFinanceProvider({ displayName: name }));
      this.toastService.success(`Finance provider '${name}' added.`);
      this.newProviderName.set('');
      await this.loadFinanceProviders();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to add finance provider.';
      this.toastService.error(msg);
    }
  }

  async removeFinanceProvider(provider: FinanceProviderItem): Promise<void> {
    const name = provider.normalizedName || (provider.displayName ? provider.displayName.toLowerCase().replace(/\s+/g, '-') : '');
    if (!name) return;
    try {
      await firstValueFrom(this.settingsClient.removeFinanceProvider(name));
      this.toastService.success(`Finance provider '${provider.displayName || name}' removed.`);
      await this.loadFinanceProviders();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to remove finance provider.';
      this.toastService.error(msg);
    }
  }

  async applyTheme(theme: 'light' | 'dark'): Promise<void> {
    this.currentTheme.set(theme);
    // HARD CONSTRAINT: Actually change data-theme attribute on document root
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('storeos_theme', theme);

    try {
      await firstValueFrom(this.settingsClient.patchSetting('theme', { theme }));
      this.toastService.success(`Theme switched to ${theme} mode.`);
    } catch {
      // Keep local visual change even if API call errors
    }
  }

  async saveRepairsEnabled(): Promise<void> {
    this.isSaving.set(true);
    try {
      const enabled = this.repairsEnabled();
      await firstValueFrom(this.settingsClient.setRepairsEnabled({ enabled }));
      this.toastService.success(`Repairs module ${enabled ? 'enabled' : 'disabled'}.`);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update repairs setting.';
      this.toastService.error(msg);
    } finally {
      this.isSaving.set(false);
    }
  }

  async changePassword(): Promise<void> {
    const cur = this.currentPassword();
    const next = this.newPassword();
    const conf = this.confirmPassword();

    if (!cur || !next) {
      this.toastService.error('Current and new password are required.');
      return;
    }
    if (next !== conf) {
      this.toastService.error('New passwords do not match.');
      return;
    }

    this.isSaving.set(true);
    try {
      const command: ChangePasswordCommand = {
        currentPassword: cur,
        newPassword: next
      };
      await firstValueFrom(this.authClient.changePassword(command));
      this.toastService.success('Password changed successfully. Please log in again.');
      // REVISION NOTE: On 204 clear the in-memory auth state and navigate to /login
      this.authStore.clear();
      await this.router.navigate(['/login']);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to change password.';
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
