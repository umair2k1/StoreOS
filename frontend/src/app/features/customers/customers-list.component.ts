import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { CustomersClient } from '../../core/api/generated-client';
import type {
  CustomerListItemDto,
  CreateCustomerCommand,
  PagedResultOfCustomerListItemDto
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PzMoneyAliasPipe } from '../../shared';

@Component({
  selector: 'app-customers-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PzMoneyAliasPipe],
  templateUrl: './customers-list.component.html',
  styleUrl: './customers-list.component.scss'
})
export class CustomersListComponent implements OnInit {
  private readonly customersClient = inject(CustomersClient);
  private readonly toastService = inject(ToastService);

  readonly searchQuery = signal<string>('');
  readonly page = signal<number>(1);
  readonly pageSize = signal<number>(20);
  readonly totalCount = signal<number>(0);
  readonly customers = signal<CustomerListItemDto[]>([]);

  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Create customer modal state
  readonly isCreateModalOpen = signal<boolean>(false);
  readonly isCreating = signal<boolean>(false);
  readonly newName = signal<string>('');
  readonly newPhone = signal<string>('');
  readonly newGstin = signal<string>('');

  ngOnInit(): void {
    this.loadCustomers();
  }

  async loadCustomers(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    const searchParam = this.searchQuery().trim() || undefined;

    try {
      const result: PagedResultOfCustomerListItemDto = await firstValueFrom(
        this.customersClient.getCustomers(searchParam, this.page(), this.pageSize())
      );
      this.customers.set(result.items || []);
      this.totalCount.set(result.totalCount || 0);
    } catch {
      this.errorMessage.set('Failed to load customers.');
    } finally {
      this.isLoading.set(false);
    }
  }

  async onSearch(val: string): Promise<void> {
    this.searchQuery.set(val);
    this.page.set(1);
    await this.loadCustomers();
  }

  async onPageChange(newPage: number): Promise<void> {
    if (newPage < 1) return;
    this.page.set(newPage);
    await this.loadCustomers();
  }

  openCreateModal(): void {
    this.newName.set('');
    this.newPhone.set('');
    this.newGstin.set('');
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  async submitCreateCustomer(): Promise<void> {
    const name = this.newName().trim();
    if (!name || this.isCreating()) {
      return;
    }

    this.isCreating.set(true);
    const command: CreateCustomerCommand = {
      name: name,
      phone: this.newPhone().trim() || undefined,
      gstin: this.newGstin().trim() || undefined
    };

    try {
      await firstValueFrom(this.customersClient.createCustomer(command));
      this.toastService.success(`Customer "${name}" created successfully.`);
      this.closeCreateModal();
      this.page.set(1);
      await this.loadCustomers();
    } catch {
      this.toastService.error('Failed to create customer.');
    } finally {
      this.isCreating.set(false);
    }
  }
}
