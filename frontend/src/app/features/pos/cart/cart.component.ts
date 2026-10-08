import {
  Component,
  HostListener,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CartStateService } from './cart-state.service';
import type { CartItem } from './cart-state.service';
import { EmptyStateComponent } from '../../../shared/components/empty-state/empty-state.component';
import { PzMoneyAliasPipe } from '../../../shared/pipes/money.pipe';
import { PermissionDirective } from '../../../core/directives/permission.directive';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    EmptyStateComponent,
    PzMoneyAliasPipe,
    PermissionDirective
  ],
  templateUrl: './cart.component.html',
  styleUrl: './cart.component.scss'
})
export class CartComponent {
  readonly cartState = inject(CartStateService);

  selectRow(id: string): void {
    this.cartState.selectItem(id);
  }

  onOverrideChange(id: string, value: string): void {
    const trimmed = value.trim();
    if (trimmed.length === 0) {
      this.cartState.setOverridePrice(id, undefined);
      return;
    }
    const num = Number(trimmed);
    if (!Number.isNaN(num) && num >= 0) {
      this.cartState.setOverridePrice(id, num);
    }
  }

  getItemLineTotal(item: CartItem): number {
    if (item.isGift) {
      return 0;
    }
    const price = item.overridePricePerUnit !== undefined && item.overridePricePerUnit !== null
      ? item.overridePricePerUnit
      : item.unitPrice;
    return price * item.qty;
  }

  @HostListener('window:keydown', ['$event'])
  handleCartHotkeys(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const isTextInput = target && (
      (target.tagName === 'INPUT' && (target as HTMLInputElement).type !== 'checkbox') ||
      target.tagName === 'TEXTAREA'
    );
    if (isTextInput) {
      return;
    }

    const selectedId = this.cartState.selectedItemId();
    if (!selectedId) {
      return;
    }

    if (event.key === '+' || event.key === '=') {
      event.preventDefault();
      this.cartState.increaseQty(selectedId);
    } else if (event.key === '-' || event.key === '_') {
      event.preventDefault();
      this.cartState.decreaseQty(selectedId);
    } else if (event.key === 'Delete') {
      event.preventDefault();
      this.cartState.removeItem(selectedId);
    }
  }
}
