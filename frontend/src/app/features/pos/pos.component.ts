import {
  Component,
  ElementRef,
  ViewChild,
  AfterViewInit,
  HostListener,
  signal,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { Subject, merge, map, debounceTime, distinctUntilChanged, switchMap, of, catchError, tap } from 'rxjs';
import { PosClient } from '../../core/api/generated-client';
import type {
  PosSearchResultDto,
  CustomerListItemDto,
  CheckoutPayment,
  CheckoutFinanceDetails
} from '../../core/api/generated-client';
import { CustomerSelectComponent } from './customer-select/customer-select.component';
import { CartComponent } from './cart/cart.component';
import { CartStateService } from './cart/cart-state.service';
import { PaymentPanelComponent } from './payment/payment-panel.component';
import { CheckoutService } from './checkout.service';
import {
  LoadingStateComponent,
  EmptyStateComponent,
  ErrorStateComponent,
  PzMoneyAliasPipe
} from '../../shared';

@Component({
  selector: 'app-pos',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    LoadingStateComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    PzMoneyAliasPipe,
    CustomerSelectComponent,
    CartComponent,
    PaymentPanelComponent
  ],
  templateUrl: './pos.component.html',
  styleUrl: './pos.component.scss'
})
export class PosComponent implements AfterViewInit {
  @ViewChild('searchInput') searchInputRef!: ElementRef<HTMLInputElement>;

  private readonly posApi = inject(PosClient);
  readonly cartState = inject(CartStateService);
  readonly checkoutService = inject(CheckoutService);
  readonly payments = signal<CheckoutPayment[]>([]);
  readonly financeDetails = signal<CheckoutFinanceDetails | null>(null);

  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly isLoading = signal(false);
  readonly errorMessage = signal<string | null>(null);
  private readonly refreshTrigger$ = new Subject<void>();
  readonly selectedCustomer = signal<CustomerListItemDto | null>(null);
  readonly cartTotal = signal<number>(0);
  readonly searchResults = toSignal(
    merge(
      this.searchControl.valueChanges.pipe(
        debounceTime(300),
        distinctUntilChanged()
      ),
      this.refreshTrigger$.pipe(
        map(() => this.searchControl.value)
      )
    ).pipe(
      switchMap(q => {
        const query = q.trim();
        if (query.length === 0) {
          this.isLoading.set(false);
          this.errorMessage.set(null);
          return of<PosSearchResultDto[]>([]);
        }
        this.isLoading.set(true);
        this.errorMessage.set(null);
        return this.posApi.search(query).pipe(
          tap(() => this.isLoading.set(false)),
          catchError(() => {
            this.isLoading.set(false);
            this.errorMessage.set('Failed to search products');
            return of<PosSearchResultDto[]>([]);
          })
        );
      })
    ),
    { initialValue: [] as PosSearchResultDto[] }
  );

  private lastKeyTime = 0;
  scanBuffer = '';

  ngAfterViewInit(): void {
    this.searchInputRef?.nativeElement.focus();
  }

  onSearchKeydown(event: KeyboardEvent): void {
    const now = performance.now();
    const delta = now - this.lastKeyTime;
    this.lastKeyTime = now;

    if (delta < 50 && event.key !== 'Enter') {
      this.scanBuffer += event.key; // fast keystrokes -> likely a scanner, not human typing
    } else if (event.key === 'Enter' && this.scanBuffer.length > 3) {
      // Scanner-speed input ending in Enter: treat as a completed barcode/IMEI scan.
      event.preventDefault();
      this.searchControl.setValue(this.scanBuffer);
      this.scanBuffer = '';
    } else {
      this.scanBuffer = event.key !== 'Enter' && event.key.length === 1 ? event.key : '';
    }
  }

  @HostListener('window:keydown', ['$event'])
  handleHotkeys(event: KeyboardEvent): void {
    if (event.key === 'F2') {
      event.preventDefault();
      this.searchInputRef?.nativeElement.focus();
    }
    if (event.key === 'F4') {
      event.preventDefault();
      this.submitCheckout();
    }
  }
  retrySearch(): void {
    this.refreshTrigger$.next();
  }

  clearSearch(): void {
    this.searchControl.setValue('');
    this.searchInputRef?.nativeElement.focus();
  }

  onCustomerSelected(customer: CustomerListItemDto | null): void {
    this.selectedCustomer.set(customer);
  }

  addToCart(item: PosSearchResultDto): void {
    if (!item.productId || !item.name) {
      return;
    }
    this.cartState.addItem({
      productId: item.productId,
      name: item.name,
      category: item.category,
      sellPrice: item.sellPrice,
      stockQty: item.stockQty
    });
  }

  onPaymentsChange(payments: CheckoutPayment[]): void {
    this.payments.set(payments);
  }

  onFinanceChange(finance: CheckoutFinanceDetails | null): void {
    this.financeDetails.set(finance);
  }

  async submitCheckout(): Promise<void> {
    await this.checkoutService.checkout({
      customer: this.selectedCustomer(),
      payments: this.payments(),
      finance: this.financeDetails(),
      invoiceDiscount: 0
    });
  }
}
