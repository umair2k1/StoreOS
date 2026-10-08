import { Injectable, signal, computed, inject } from '@angular/core';
import { ToastService } from '../../../shared/services/toast.service';

export interface CartProductInput {
  productId: string;
  name: string;
  category?: string;
  sellPrice?: number;
  stockQty?: number;
}

export interface CartItem {
  id: string;
  productId: string;
  name: string;
  category?: string;
  unitPrice: number;
  stockQty?: number;
  qty: number;
  imei?: string;
  useMarginScheme: boolean;
  isGift: boolean;
  overridePricePerUnit?: number;
}

@Injectable({
  providedIn: 'root'
})
export class CartStateService {
  private readonly toastService = inject(ToastService);

  readonly items = signal<CartItem[]>([]);
  readonly selectedItemId = signal<string | null>(null);

  readonly totalQty = computed(() =>
    this.items().reduce((total, item) => total + item.qty, 0)
  );

  readonly subtotal = computed(() =>
    this.items().reduce((total, item) => {
      if (item.isGift) {
        return total;
      }
      const price = item.overridePricePerUnit !== undefined && item.overridePricePerUnit !== null
        ? item.overridePricePerUnit
        : item.unitPrice;
      return total + price * item.qty;
    }, 0)
  );

  addItem(product: CartProductInput, imei?: string, isGift = false): void {
    const currentItems = this.items();
    const isUsedPhone = product.category === 'UsedPhone';

    // Rule: IMEI-bearing lines are ALWAYS quantity 1 and NEVER merge with any other line
    if (imei && imei.trim().length > 0) {
      const newLine: CartItem = {
        id: crypto.randomUUID(),
        productId: product.productId,
        name: product.name,
        category: product.category,
        unitPrice: product.sellPrice ?? 0,
        stockQty: product.stockQty,
        qty: 1,
        imei: imei.trim(),
        useMarginScheme: isUsedPhone,
        isGift,
        overridePricePerUnit: undefined
      };
      this.items.set([...currentItems, newLine]);
      this.selectedItemId.set(newLine.id);
      return;
    }

    // Rule: Adding existing (non-IMEI, non-gift) item increases quantity rather than duplicating
    if (!isGift) {
      const existingIndex = currentItems.findIndex(
        item => item.productId === product.productId && !item.imei && !item.isGift
      );

      if (existingIndex >= 0) {
        const existing = currentItems[existingIndex];
        const newQty = existing.qty + 1;

        // Soft client-side stock guard with toast warning
        if (existing.category !== 'Service' && existing.stockQty !== undefined && existing.stockQty !== null && newQty > existing.stockQty) {
          this.toastService.warning(`Requested quantity (${newQty}) exceeds reported stock (${existing.stockQty}) for ${existing.name}.`);
        }

        const updated = [...currentItems];
        updated[existingIndex] = { ...existing, qty: newQty };
        this.items.set(updated);
        this.selectedItemId.set(existing.id);
        return;
      }
    }

    // Otherwise create new standard/gift line
    const newLine: CartItem = {
      id: crypto.randomUUID(),
      productId: product.productId,
      name: product.name,
      category: product.category,
      unitPrice: product.sellPrice ?? 0,
      stockQty: product.stockQty,
      qty: 1,
      imei: undefined,
      useMarginScheme: isUsedPhone,
      isGift,
      overridePricePerUnit: undefined
    };

    if (newLine.category !== 'Service' && newLine.stockQty !== undefined && newLine.stockQty !== null && newLine.stockQty <= 0) {
      this.toastService.warning(`Requested item ${newLine.name} has zero reported stock.`);
    }

    this.items.set([...currentItems, newLine]);
    this.selectedItemId.set(newLine.id);
  }

  increaseQty(id: string): void {
    const current = this.items();
    const index = current.findIndex(i => i.id === id);
    if (index === -1) {
      return;
    }

    const item = current[index];
    // IMEI lines are strictly single-unit
    if (item.imei) {
      return;
    }

    const newQty = item.qty + 1;
    if (item.category !== 'Service' && item.stockQty !== undefined && item.stockQty !== null && newQty > item.stockQty) {
      this.toastService.warning(`Requested quantity (${newQty}) exceeds reported stock (${item.stockQty}) for ${item.name}.`);
    }

    const updated = [...current];
    updated[index] = { ...item, qty: newQty };
    this.items.set(updated);
  }

  decreaseQty(id: string): void {
    const current = this.items();
    const index = current.findIndex(i => i.id === id);
    if (index === -1) {
      return;
    }

    const item = current[index];
    if (item.imei || item.qty <= 1) {
      // Rule: Quantity cannot be decreased below 1 (use remove action instead)
      return;
    }

    const updated = [...current];
    updated[index] = { ...item, qty: item.qty - 1 };
    this.items.set(updated);
  }

  removeItem(id: string): void {
    const current = this.items();
    const updated = current.filter(i => i.id !== id);
    this.items.set(updated);

    if (this.selectedItemId() === id) {
      this.selectedItemId.set(updated.length > 0 ? updated[0].id : null);
    }
  }

  setMarginScheme(id: string, useMargin: boolean): void {
    const current = this.items();
    const index = current.findIndex(i => i.id === id);
    if (index === -1) {
      return;
    }

    const item = current[index];
    // Rule: Setting margin scheme on a non-UsedPhone line is rejected/ignored
    if (item.category !== 'UsedPhone') {
      return;
    }

    const updated = [...current];
    updated[index] = { ...item, useMarginScheme: useMargin };
    this.items.set(updated);
  }

  setGift(id: string, isGift: boolean): void {
    const current = this.items();
    const index = current.findIndex(i => i.id === id);
    if (index === -1) {
      return;
    }

    const updated = [...current];
    updated[index] = { ...current[index], isGift };
    this.items.set(updated);
  }

  setOverridePrice(id: string, price: number | undefined): void {
    const current = this.items();
    const index = current.findIndex(i => i.id === id);
    if (index === -1) {
      return;
    }

    const updated = [...current];
    updated[index] = { ...current[index], overridePricePerUnit: price };
    this.items.set(updated);
  }

  selectItem(id: string): void {
    this.selectedItemId.set(id);
  }

  clear(): void {
    this.items.set([]);
    this.selectedItemId.set(null);
  }
}
