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
  PzMoneyAliasPipe,
  PzDialogService
} from '../../shared';
import { PrintInvoiceDialogComponent } from './print-invoice-dialog.component';
import type { PrintInvoiceDialogData } from './print-invoice-dialog.component';

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
  private readonly dialogService = inject(PzDialogService);
  readonly cartState = inject(CartStateService);
  readonly checkoutService = inject(CheckoutService);
  readonly autoPrintInvoice = signal<boolean>(false);
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

  toggleAutoPrint(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.autoPrintInvoice.set(input.checked);
  }

  async submitCheckout(): Promise<void> {
    const activePayments = this.payments().filter(p => (p.amount ?? 0) > 0);
    const paymentsToSend = activePayments.length > 0
      ? activePayments
      : [{ paymentMode: 'Cash', amount: this.financeDetails() ? (this.financeDetails()?.downPayment ?? 0) : this.cartState.subtotal() }];
    const shouldAutoPrint = this.autoPrintInvoice();
    const result = await this.checkoutService.checkout({
      customer: this.selectedCustomer(),
      payments: paymentsToSend,
      finance: this.financeDetails(),
      invoiceDiscount: 0,
      autoPrint: shouldAutoPrint
    });

    if (result.success && !shouldAutoPrint && result.saleId) {
      this.promptForPrintout(result.saleId, result.invoiceNumber ?? 'Invoice');
    }
    this.autoPrintInvoice.set(false);
  }

  private promptForPrintout(saleId: string, invoiceNumber: string): void {
    const dialogRef = this.dialogService.open<PrintInvoiceDialogComponent, PrintInvoiceDialogData, boolean>(
      PrintInvoiceDialogComponent,
      {
        data: {
          saleId,
          invoiceNumber
        }
      }
    );

    dialogRef.afterClosed().subscribe((confirmed) => {
      if (confirmed) {
        this.checkoutService.printInvoice(saleId);
      }
    });
  }
}
