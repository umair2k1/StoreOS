import {
  Component,
  Input,
  Output,
  EventEmitter,
  signal,
  computed,
  inject
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule, FormsModule } from '@angular/forms';
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
    FormsModule,
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
  readonly customPhoneInput = signal<string>('');

  readonly walkInCustomer = WALK_IN_CUSTOMER;

  readonly isDefaultWalkIn = computed(() => {
    const cust = this.selectedCustomer();
    return !cust.id && (!cust.name || cust.name === 'Walk-in Customer');
  });

  readonly isCustomWalkIn = computed(() => {
    const cust = this.selectedCustomer();
    return !cust.id && !!cust.name && cust.name !== 'Walk-in Customer';
  });

  readonly isWalkIn = computed(() => !this.selectedCustomer().id);

  readonly currentSearchTerm = toSignal(
    this.searchControl.valueChanges,
    { initialValue: '' }
  );

  readonly customNameOption = computed(() => {
    const term = (this.currentSearchTerm() || '').trim();
    if (!term || term.toLowerCase() === 'walk-in' || term.toLowerCase() === 'walk-in customer') {
      return null;
    }
    const exactMatch = (this.searchResults() || []).some(
      c => c.name?.trim().toLowerCase() === term.toLowerCase()
    );
    return exactMatch ? null : term;
  });

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

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      const customName = this.customNameOption();
      if (customName) {
        event.preventDefault();
        this.selectCustomName(customName);
      }
    } else if (event.key === 'Escape') {
      this.isDropdownOpen.set(false);
    }
  }

  selectCustomName(name: string): void {
    const phone = this.customPhoneInput().trim();
    const customCustomer: CustomerListItemDto = {
      id: '',
      name: name.trim(),
      phone: phone.length > 0 ? phone : undefined,
      totalPurchases: 0,
      currentCreditBalance: 0
    };
    this.selectedCustomer.set(customCustomer);
    this.creditBalance.set(null);
    this.isDropdownOpen.set(false);
    this.searchControl.setValue('', { emitEvent: false });
    this.customerSelected.emit(customCustomer);
  }

  onCustomPhoneChange(phone: string): void {
    this.customPhoneInput.set(phone);
    const current = this.selectedCustomer();
    if (!current.id && current.name && current.name !== 'Walk-in Customer') {
      const updated: CustomerListItemDto = {
        ...current,
        phone: phone.trim().length > 0 ? phone.trim() : undefined
      };
      this.selectedCustomer.set(updated);
      this.customerSelected.emit(updated);
    }
  }

  selectCustomer(customer: CustomerListItemDto): void {
    this.selectedCustomer.set(customer);
    this.customPhoneInput.set(customer.phone || '');
    this.isDropdownOpen.set(false);
    this.searchControl.setValue('', { emitEvent: false });
    if (!customer.id) {
      this.creditBalance.set(null);
      this.customerSelected.emit(customer);
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
    this.customPhoneInput.set('');
    this.selectCustomer(WALK_IN_CUSTOMER);
  }
}
