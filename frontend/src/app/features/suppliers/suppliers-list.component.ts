import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SuppliersClient } from '../../core/api/generated-client';
import type {
  CreateSupplierCommand,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

export interface SupplierListItem {
  id: string;
  name: string;
  phone?: string;
  address?: string;
  brandOrCategory?: string;
}

@Component({
  selector: 'app-suppliers-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './suppliers-list.component.html',
  styleUrl: './suppliers-list.component.scss'
})
export class SuppliersListComponent implements OnInit {
  private readonly suppliersClient = inject(SuppliersClient);
  private readonly toastService = inject(ToastService);

  readonly searchQuery = signal<string>('');
  readonly page = signal<number>(1);
  readonly pageSize = signal<number>(20);
  readonly suppliers = signal<SupplierListItem[]>([]);

  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Create Supplier modal state
  readonly isCreateModalOpen = signal<boolean>(false);
  readonly isCreating = signal<boolean>(false);
  readonly newName = signal<string>('');
  readonly newPhone = signal<string>('');
  readonly newAddress = signal<string>('');
  readonly newCategory = signal<string>('');

  ngOnInit(): void {
    this.loadSuppliers();
  }

  async loadSuppliers(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    const searchParam = this.searchQuery().trim() || undefined;

    try {
      const resp = await firstValueFrom(
        this.suppliersClient.list(searchParam, this.page(), this.pageSize())
      );
      const parsed = await this.parseFileResponse<unknown>(resp);
      const list: SupplierListItem[] = [];

      let rawItems: unknown[] = [];
      if (Array.isArray(parsed)) {
        rawItems = parsed;
      } else if (parsed && typeof parsed === 'object' && 'items' in parsed) {
        const nested = parsed.items;
        if (Array.isArray(nested)) {
          rawItems = nested;
        }
      }

      for (const item of rawItems) {
        if (item && typeof item === 'object') {
          let sId = '';
          if ('id' in item && typeof item.id === 'string') {
            sId = item.id;
          }
          let sName = 'Unknown Supplier';
          if ('name' in item && typeof item.name === 'string') {
            sName = item.name;
          }
          let sPhone: string | undefined;
          if ('phone' in item && typeof item.phone === 'string') {
            sPhone = item.phone;
          }
          let sAddress: string | undefined;
          if ('address' in item && typeof item.address === 'string') {
            sAddress = item.address;
          }
          let sCategory: string | undefined;
          if ('brandOrCategory' in item && typeof item.brandOrCategory === 'string') {
            sCategory = item.brandOrCategory;
          }

          if (sId) {
            list.push({
              id: sId,
              name: sName,
              phone: sPhone,
              address: sAddress,
              brandOrCategory: sCategory
            });
          }
        }
      }

      this.suppliers.set(list);
    } catch {
      this.errorMessage.set('Failed to load suppliers.');
    } finally {
      this.isLoading.set(false);
    }
  }

  async onSearch(val: string): Promise<void> {
    this.searchQuery.set(val);
    this.page.set(1);
    await this.loadSuppliers();
  }

  async onPageChange(newPage: number): Promise<void> {
    if (newPage < 1) return;
    this.page.set(newPage);
    await this.loadSuppliers();
  }

  openCreateModal(): void {
    this.newName.set('');
    this.newPhone.set('');
    this.newAddress.set('');
    this.newCategory.set('');
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  async submitCreateSupplier(): Promise<void> {
    const name = this.newName().trim();
    if (!name || this.isCreating()) return;

    this.isCreating.set(true);
    const command: CreateSupplierCommand = {
      name: name,
      phone: this.newPhone().trim() || undefined,
      address: this.newAddress().trim() || undefined,
      brandOrCategory: this.newCategory().trim() || undefined
    };

    try {
      await firstValueFrom(this.suppliersClient.create(command));
      this.toastService.success(`Supplier "${name}" created.`);
      this.closeCreateModal();
      this.page.set(1);
      await this.loadSuppliers();
    } catch {
      this.toastService.error('Failed to create supplier.');
    } finally {
      this.isCreating.set(false);
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp) return null;
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      if (!text) return null;
      const parsedUnknown = JSON.parse(text) as unknown;
      return parsedUnknown as T;
    }
    if (typeof resp.data === 'string') {
      const parsedUnknown = JSON.parse(resp.data) as unknown;
      return parsedUnknown as T;
    }
    const rawData = resp.data as unknown;
    return (rawData as T) || null;
  }
}
