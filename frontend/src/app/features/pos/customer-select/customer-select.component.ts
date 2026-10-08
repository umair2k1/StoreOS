import {
  Component,
  Input,
  Output,
  EventEmitter,
  signal,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged, switchMap, of, catchError, tap } from 'rxjs';
import { CustomersClient } from '../../../core/api/generated-client';
import type { CustomerListItemDto, CreditBalanceDto } from '../../../core/api/generated-client';
import { PzDialogService } from '../../../shared/components/pz-dialog/pz-dialog.service';
import { PzMoneyAliasPipe } from '../../../shared/pipes/money.pipe';
import { CreditLimitWarningDialogComponent } from './credit-limit-warning-dialog.component';

export const WALK_IN_CUSTOMER: CustomerListItemDto = {
  id: '',
  name: 'Walk-in Customer',
  phone: undefined,
  totalPurchases: 0,
  currentCreditBalance: 0
};

@Component({
  selector: 'app-customer-select',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    PzMoneyAliasPipe
  ],
  templateUrl: './customer-select.component.html',
  styleUrl: './customer-select.component.scss'
})
export class CustomerSelectComponent {
  private readonly customersApi = inject(CustomersClient);
  private readonly dialogService = inject(PzDialogService);

  @Input() set cartTotal(total: number) {
    this._cartTotal.set(total);
    this.evaluateCreditLimit(total);
  }
  get cartTotal(): number {
    return this._cartTotal();
  }
  private readonly _cartTotal = signal<number>(0);

  @Output() readonly customerSelected = new EventEmitter<CustomerListItemDto | null>();

  readonly searchControl = new FormControl('', { nonNullable: true });
  readonly isSearching = signal(false);
  readonly isFetchingBalance = signal(false);
  readonly isDropdownOpen = signal(false);
  readonly selectedCustomer = signal<CustomerListItemDto>(WALK_IN_CUSTOMER);
  readonly creditBalance = signal<CreditBalanceDto | null>(null);

  readonly walkInCustomer = WALK_IN_CUSTOMER;

  readonly searchResults = toSignal(
    this.searchControl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap(q => {
        const query = q.trim();
        if (query.length === 0) {
          this.isSearching.set(false);
          return of<CustomerListItemDto[]>([]);
        }
        this.isSearching.set(true);
        return this.customersApi.getCustomers(query, 1, 10).pipe(
          tap(() => this.isSearching.set(false)),
          switchMap(result => of(result.items ?? [])),
          catchError(() => {
            this.isSearching.set(false);
            return of<CustomerListItemDto[]>([]);
          })
        );
      })
    ),
    { initialValue: [] as CustomerListItemDto[] }
  );

  onSearchFocus(): void {
    this.isDropdownOpen.set(true);
  }

  selectCustomer(customer: CustomerListItemDto): void {
    this.selectedCustomer.set(customer);
    this.isDropdownOpen.set(false);
    this.searchControl.setValue('', { emitEvent: false });

    if (!customer.id) {
      this.creditBalance.set(null);
      this.customerSelected.emit(null);
      return;
    }

    this.customerSelected.emit(customer);
    this.isFetchingBalance.set(true);
    this.customersApi.getCreditBalance(customer.id).pipe(
      catchError(() => {
        this.isFetchingBalance.set(false);
        return of<CreditBalanceDto | null>(null);
      })
    ).subscribe(balance => {
      this.isFetchingBalance.set(false);
      this.creditBalance.set(balance);
      this.evaluateCreditLimit(this._cartTotal());
    });
  }

  evaluateCreditLimit(cartTotal: number = this._cartTotal()): boolean {
    const balance = this.creditBalance();
    if (!balance || balance.creditLimit === undefined || balance.creditLimit === null || balance.creditLimit <= 0) {
      return true;
    }

    const currentBal = balance.consolidatedBalance ?? 0;
    const projected = currentBal + cartTotal;
    if (projected > balance.creditLimit) {
      this.dialogService.open(CreditLimitWarningDialogComponent, {
        data: {
          customerName: this.selectedCustomer()?.name ?? 'Selected Customer',
          currentBalance: currentBal,
          cartTotal,
          creditLimit: balance.creditLimit
        }
      });
      return false;
    }
    return true;
  }

  clearCustomer(): void {
    this.selectCustomer(WALK_IN_CUSTOMER);
  }
}
