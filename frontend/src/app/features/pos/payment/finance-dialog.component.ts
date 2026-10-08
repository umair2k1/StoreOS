import {
  Component,
  OnInit,
  signal,
  computed,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { PzDialogComponent } from '../../../shared/components/pz-dialog/pz-dialog.component';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';
import { PzMoneyAliasPipe } from '../../../shared/pipes/money.pipe';
import { SettingsClient } from '../../../core/api/generated-client';
import type { FinanceProviderDto, CheckoutFinanceDetails } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import { calculatePrincipal, calculateEmi, round2AwayFromZero } from './emi-calculator';

export interface FinanceDialogData {
  grandTotal: number;
  initialDownPayment?: number;
  initialTenureMonths?: number;
  initialInterestRate?: number;
}

export interface EmiPreviewResult {
  principal: number;
  emi: number;
  totalPayable: number;
  error: string | null;
}

@Component({
  selector: 'app-finance-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    PzDialogComponent,
    PzMoneyAliasPipe
  ],
  templateUrl: './finance-dialog.component.html',
  styleUrl: './finance-dialog.component.scss'
})
export class FinanceDialogComponent implements OnInit {
  readonly dialogRef = inject(PzDialogRef);
  readonly data = inject<FinanceDialogData>(PZ_DIALOG_DATA);
  private readonly settingsClient = inject(SettingsClient);
  private readonly toastService = inject(ToastService);

  readonly providers = signal<FinanceProviderDto[]>([]);
  readonly isLoadingProviders = signal(false);

  readonly selectedProvider = signal<string>('');
  readonly customFinancerName = signal<string>('');
  readonly downPayment = signal<number>(this.data.initialDownPayment ?? 0);
  readonly tenureMonths = signal<number>(this.data.initialTenureMonths ?? 12);
  readonly annualInterestRate = signal<number>(this.data.initialInterestRate ?? 0);

  readonly isAddingProvider = signal(false);
  readonly newProviderName = signal('');
  readonly isSavingProvider = signal(false);

  readonly emiPreview = computed<EmiPreviewResult>(() => {
    try {
      const total = this.data.grandTotal;
      const dp = this.downPayment();
      const tenure = this.tenureMonths();
      const rate = this.annualInterestRate();

      const principal = calculatePrincipal(total, dp);
      const emi = calculateEmi(principal, rate, tenure);
      const totalPayable = round2AwayFromZero(dp + emi * tenure);

      return {
        principal,
        emi,
        totalPayable,
        error: null
      };
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : 'Invalid finance parameters';
      return {
        principal: 0,
        emi: 0,
        totalPayable: 0,
        error: msg
      };
    }
  });

  ngOnInit(): void {
    this.loadProviders();
  }

  loadProviders(selectName?: string): void {
    this.isLoadingProviders.set(true);
    this.settingsClient.getFinanceProviders().subscribe({
      next: (list) => {
        this.isLoadingProviders.set(false);
        this.providers.set(list);
        if (selectName) {
          const matched = list.find(p => p.normalizedName === selectName || p.displayName === selectName);
          if (matched) {
            this.selectedProvider.set(matched.normalizedName ?? matched.displayName ?? '');
          }
        } else if (!this.selectedProvider() && list.length > 0) {
          this.selectedProvider.set(list[0].normalizedName ?? list[0].displayName ?? '');
        }
      },
      error: () => {
        this.isLoadingProviders.set(false);
        this.toastService.error('Failed to load finance providers');
      }
    });
  }

  toggleAddProvider(): void {
    this.isAddingProvider.set(!this.isAddingProvider());
    this.newProviderName.set('');
  }

  saveNewProvider(): void {
    const name = this.newProviderName().trim();
    if (name.length === 0) {
      return;
    }

    this.isSavingProvider.set(true);
    this.settingsClient.addFinanceProvider({ displayName: name }).subscribe({
      next: () => {
        this.isSavingProvider.set(false);
        this.isAddingProvider.set(false);
        this.toastService.success(`Finance provider "${name}" added.`);
        this.loadProviders(name);
      },
      error: () => {
        this.isSavingProvider.set(false);
        this.toastService.error('Failed to add finance provider.');
      }
    });
  }

  onCancel(): void {
    this.dialogRef.close(null);
  }

  onConfirm(): void {
    const preview = this.emiPreview();
    if (preview.error) {
      this.toastService.error(preview.error);
      return;
    }

    const provider = this.selectedProvider().trim();
    if (provider.length === 0) {
      this.toastService.warning('Please select or specify a finance provider.');
      return;
    }

    const details: CheckoutFinanceDetails = {
      financeProvider: provider,
      financerName: this.customFinancerName().trim() || undefined,
      downPayment: this.downPayment(),
      tenureMonths: this.tenureMonths(),
      annualInterestRate: this.annualInterestRate()
    };

    this.dialogRef.close(details);
  }
}
