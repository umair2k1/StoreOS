import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CustomersClient } from '../../../core/api/generated-client';
import type {
  CustomerDetailDto,
  CreditBalanceDto,
  SaleSummaryDto,
  CustomerLedgerEntryDto,
  UpdateCustomerProfileRequest,
  SetCustomerCreditLimitRequest
} from '../../../core/api/generated-client';
import { PermissionDirective } from '../../../core/directives/permission.directive';
import { ToastService } from '../../../shared/services/toast.service';
import { PzMoneyAliasPipe, PzDateAliasPipe } from '../../../shared';

@Component({
  selector: 'app-customer-detail',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    PermissionDirective,
    PzMoneyAliasPipe,
    PzDateAliasPipe
  ],
  templateUrl: './customer-detail.component.html',
  styleUrl: './customer-detail.component.scss'
})
export class CustomerDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly customersClient = inject(CustomersClient);
  private readonly toastService = inject(ToastService);

  readonly customerId = signal<string>('');
  readonly customer = signal<CustomerDetailDto | null>(null);
  readonly creditBalance = signal<CreditBalanceDto | null>(null);
  readonly salesHistory = signal<SaleSummaryDto[]>([]);
  readonly ledgerEntries = signal<CustomerLedgerEntryDto[]>([]);

  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Edit Profile modal state (gated by pos:checkout)
  readonly isEditProfileModalOpen = signal<boolean>(false);
  readonly isUpdatingProfile = signal<boolean>(false);
  readonly editName = signal<string>('');
  readonly editPhone = signal<string>('');
  readonly editGstin = signal<string>('');

  // Edit Credit Limit modal state (gated by settings:manage)
  readonly isEditCreditLimitModalOpen = signal<boolean>(false);
  readonly isUpdatingCreditLimit = signal<boolean>(false);
  readonly newCreditLimit = signal<number>(0);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.customerId.set(id);
      this.loadAllData(id);
    } else {
      this.errorMessage.set('Missing customer identifier in route.');
    }
  }

  async loadAllData(id: string): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    try {
      const [cust, credit, sales, ledger] = await Promise.all([
        firstValueFrom(this.customersClient.getCustomerById(id)),
        firstValueFrom(this.customersClient.getCreditBalance(id)),
        firstValueFrom(this.customersClient.getCustomerSales(id, 1, 50)),
        firstValueFrom(this.customersClient.getCustomerLedger(id, 1, 50))
      ]);

      this.customer.set(cust);
      this.creditBalance.set(credit);
      this.salesHistory.set(sales.items || []);
      this.ledgerEntries.set(ledger.items || []);
    } catch {
      this.errorMessage.set('Failed to load customer details.');
    } finally {
      this.isLoading.set(false);
    }
  }

  // Profile Edit modal
  openEditProfileModal(): void {
    const cust = this.customer();
    if (!cust) return;
    this.editName.set(cust.name || '');
    this.editPhone.set(cust.phone || '');
    this.editGstin.set(cust.gstin || '');
    this.isEditProfileModalOpen.set(true);
  }

  closeEditProfileModal(): void {
    this.isEditProfileModalOpen.set(false);
  }

  async submitUpdateProfile(): Promise<void> {
    const id = this.customerId();
    const name = this.editName().trim();
    if (!id || !name || this.isUpdatingProfile()) return;

    this.isUpdatingProfile.set(true);
    const req: UpdateCustomerProfileRequest = {
      name: name,
      phone: this.editPhone().trim() || undefined,
      gstin: this.editGstin().trim() || undefined
    };

    try {
      await firstValueFrom(this.customersClient.updateCustomerProfile(id, req));
      this.toastService.success('Customer profile updated.');
      this.closeEditProfileModal();
      await this.loadAllData(id);
    } catch {
      this.toastService.error('Failed to update profile.');
    } finally {
      this.isUpdatingProfile.set(false);
    }
  }

  // Credit Limit Edit modal
  openEditCreditLimitModal(): void {
    const bal = this.creditBalance();
    this.newCreditLimit.set(bal?.creditLimit ?? this.customer()?.creditLimit ?? 0);
    this.isEditCreditLimitModalOpen.set(true);
  }

  closeEditCreditLimitModal(): void {
    this.isEditCreditLimitModalOpen.set(false);
  }

  async submitUpdateCreditLimit(): Promise<void> {
    const id = this.customerId();
    if (!id || this.isUpdatingCreditLimit()) return;

    this.isUpdatingCreditLimit.set(true);
    const req: SetCustomerCreditLimitRequest = {
      creditLimit: this.newCreditLimit()
    };

    try {
      await firstValueFrom(this.customersClient.setCustomerCreditLimit(id, req));
      this.toastService.success('Credit limit updated.');
      this.closeEditCreditLimitModal();
      await this.loadAllData(id);
    } catch {
      this.toastService.error('Failed to update credit limit.');
    } finally {
      this.isUpdatingCreditLimit.set(false);
    }
  }
}
