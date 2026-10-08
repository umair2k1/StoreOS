import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PosClient, PurchasesClient } from '../../../core/api/generated-client';
import type { PosProductDetailDto, FileResponse } from '../../../core/api/generated-client';
import {
  LoadingStateComponent,
  ErrorStateComponent,
  PzMoneyAliasPipe,
  PzDateAliasPipe
} from '../../../shared';
import { StockInFormComponent } from './stock-in-form.component';

export interface PurchaseHistoryItem {
  id?: string;
  purchaseDate?: Date;
  supplierName?: string;
  qty?: number;
  purchaseRate?: number;
  supplierInvoiceNo?: string;
  imeis?: string[];
}

@Component({
  selector: 'app-inventory-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    LoadingStateComponent,
    ErrorStateComponent,
    PzMoneyAliasPipe,
    PzDateAliasPipe,
    StockInFormComponent
  ],
  templateUrl: './inventory-detail.component.html',
  styleUrl: './inventory-detail.component.scss'
})
export class InventoryDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly posClient = inject(PosClient);
  private readonly purchasesClient = inject(PurchasesClient);

  readonly productId = signal<string>('');
  readonly product = signal<PosProductDetailDto | null>(null);
  readonly purchaseHistory = signal<PurchaseHistoryItem[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.productId.set(id);
      this.loadAll();
    } else {
      this.errorMessage.set('Missing product identifier in route.');
    }
  }

  async loadAll(): Promise<void> {
    const id = this.productId();
    if (!id) return;

    this.isLoading.set(true);
    this.errorMessage.set(null);

    try {
      const [prod, purchasesResp] = await Promise.all([
        firstValueFrom(this.posClient.getProduct(id)),
        firstValueFrom(this.purchasesClient.list(id))
      ]);

      this.product.set(prod);

      const parsedPurchases = await this.parsePurchasesResponse(purchasesResp);
      this.purchaseHistory.set(parsedPurchases);
      this.isLoading.set(false);
    } catch {
      this.isLoading.set(false);
      this.errorMessage.set('Failed to load inventory details.');
    }
  }

  onStockInSuccess(): void {
    this.loadAll();
  }

  getStockBadgeClass(status?: string): string {
    const val = (status || '').toLowerCase().trim();
    if (val.includes('in stock') || val.includes('instock')) return 'in-stock';
    if (val.includes('low stock') || val.includes('lowstock')) return 'low-stock';
    if (val.includes('out of stock') || val.includes('outofstock')) return 'out-of-stock';
    return '';
  }

  private async parsePurchasesResponse(resp: FileResponse): Promise<PurchaseHistoryItem[]> {
    if (!resp) return [];
    let parsed: any = [];
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      if (text) {
        try { parsed = JSON.parse(text); } catch {}
      }
    } else if (typeof resp.data === 'string') {
      try { parsed = JSON.parse(resp.data); } catch {}
    } else if (resp.data && typeof resp.data === 'object') {
      parsed = resp.data;
    }

    if (Array.isArray(parsed)) return parsed;
    if (parsed && Array.isArray(parsed.items)) return parsed.items;
    return [];
  }
}
