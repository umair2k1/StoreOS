import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { OverlayModule } from '@angular/cdk/overlay';
import { of } from 'rxjs';
import { CustomerSelectComponent } from './customer-select.component';
import { CreditLimitWarningDialogComponent } from './credit-limit-warning-dialog.component';
import { CustomersClient } from '../../../core/api/generated-client';
import type { CustomerListItemDto, CreditBalanceDto } from '../../../core/api/generated-client';
import { PzDialogService, PZ_DIALOG_DATA, PzDialogRef } from '../../../shared/components/pz-dialog/pz-dialog.service';
import { AuthSignalStore } from '../../../core/auth/auth-signal-store';

function fakeAsync(fn: () => void): () => void {
  return () => {
    vi.useFakeTimers();
    try {
      fn();
    } finally {
      vi.useRealTimers();
    }
  };
}

function tick(ms: number): void {
  vi.advanceTimersByTime(ms);
}

describe('CustomerSelectComponent', () => {
  let fixture: ComponentFixture<CustomerSelectComponent>;
  let component: CustomerSelectComponent;
  let customersClient: CustomersClient;
  let dialogService: PzDialogService;
  let authStore: AuthSignalStore;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomerSelectComponent, OverlayModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        CustomersClient,
        PzDialogService,
        AuthSignalStore
      ]
    }).compileComponents();

    customersClient = TestBed.inject(CustomersClient);
    dialogService = TestBed.inject(PzDialogService);
    authStore = TestBed.inject(AuthSignalStore);
    fixture = TestBed.createComponent(CustomerSelectComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('(a) typeahead returns matching customers with Walk-in option at the top', fakeAsync(() => {
    const mockCustomers: CustomerListItemDto[] = [
      { id: 'c-101', name: 'Raj Sharma', phone: '9876543210', currentCreditBalance: 1500 },
      { id: 'c-102', name: 'Rajesh Gupta', phone: '9876543211', currentCreditBalance: 0 }
    ];

    const getCustomersSpy = vi.spyOn(customersClient, 'getCustomers').mockReturnValue(
      of({ items: mockCustomers, totalCount: 2, page: 1, pageSize: 10 })
    );

    component.onSearchFocus();
    component.searchControl.setValue('Raj');
    tick(300);
    fixture.detectChanges();

    expect(getCustomersSpy).toHaveBeenCalledWith('Raj', 1, 10);
    expect(component.searchResults().length).toBe(2);

    const dropdown = fixture.nativeElement.querySelector('.customer-dropdown');
    expect(dropdown).toBeTruthy();

    const walkInOption = dropdown.querySelector('.walk-in-option');
    expect(walkInOption).toBeTruthy();
    expect(walkInOption.textContent).toContain('Walk-in Customer');

    const customerOptions = dropdown.querySelectorAll('[data-customer-id]');
    expect(customerOptions.length).toBe(2);
    expect(customerOptions[0].textContent).toContain('Raj Sharma');
    expect(customerOptions[1].textContent).toContain('Rajesh Gupta');
  }));

  it('(b) selecting a customer fetches and displays their balance', () => {
    const selected: CustomerListItemDto = {
      id: 'cust-201',
      name: 'Pooja Verma',
      phone: '9988776655'
    };

    const mockBalance: CreditBalanceDto = {
      customerId: 'cust-201',
      consolidatedBalance: 4500,
      creditLimit: 10000,
      availableCredit: 5500,
      isOverLimit: false
    };

    const getBalanceSpy = vi.spyOn(customersClient, 'getCreditBalance').mockReturnValue(of(mockBalance));
    const customerSelectedSpy = vi.spyOn(component.customerSelected, 'emit');

    component.selectCustomer(selected);
    fixture.detectChanges();

    expect(getBalanceSpy).toHaveBeenCalledWith('cust-201');
    expect(customerSelectedSpy).toHaveBeenCalledWith(selected);
    expect(component.selectedCustomer().name).toBe('Pooja Verma');
    expect(component.creditBalance()).toEqual(mockBalance);

    const balanceAmountEl = fixture.nativeElement.querySelector('.balance-amount');
    expect(balanceAmountEl).toBeTruthy();
    expect(balanceAmountEl.textContent).toContain('4,500');
  });

  it('(c) a cart total that would exceed the limit shows the warning dialog', () => {
    const selected: CustomerListItemDto = {
      id: 'cust-301',
      name: 'Anil Kumar',
      phone: '9123456780'
    };

    const mockBalance: CreditBalanceDto = {
      customerId: 'cust-301',
      consolidatedBalance: 8000,
      creditLimit: 10000,
      availableCredit: 2000,
      isOverLimit: false
    };

    vi.spyOn(customersClient, 'getCreditBalance').mockReturnValue(of(mockBalance));
    const dialogSpy = vi.spyOn(dialogService, 'open');

    component.selectCustomer(selected);
    fixture.detectChanges();

    // Adding cart total of 3000 -> projected 8000 + 3000 = 11000 > 10000 limit
    component.cartTotal = 3000;
    fixture.detectChanges();

    expect(dialogSpy).toHaveBeenCalledWith(CreditLimitWarningDialogComponent, {
      data: {
        customerName: 'Anil Kumar',
        currentBalance: 8000,
        cartTotal: 3000,
        creditLimit: 10000
      }
    });
  });
  it('(c1) shows option to use new customer name on the go when typing non-matching query', fakeAsync(() => {
    vi.spyOn(customersClient, 'getCustomers').mockReturnValue(
      of({ items: [], totalCount: 0, page: 1, pageSize: 10 })
    );
    const customerSelectedSpy = vi.spyOn(component.customerSelected, 'emit');

    component.onSearchFocus();
    component.searchControl.setValue('Harish Patel');
    tick(300);
    fixture.detectChanges();

    const dropdown = fixture.nativeElement.querySelector('.customer-dropdown');
    expect(dropdown).toBeTruthy();

    const customOption = dropdown.querySelector('.add-custom-option');
    expect(customOption).toBeTruthy();
    expect(customOption.textContent).toContain('Use "Harish Patel"');

    // Click custom customer option
    customOption.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();

    expect(customerSelectedSpy).toHaveBeenCalledWith(expect.objectContaining({
      id: '',
      name: 'Harish Patel'
    }));
    expect(component.selectedCustomer().name).toBe('Harish Patel');
    expect(component.isCustomWalkIn()).toBe(true);

    const nameEl = fixture.nativeElement.querySelector('.customer-name');
    expect(nameEl.textContent).toContain('Harish Patel');
    const walkInBadge = fixture.nativeElement.querySelector('.badge-walk-in-tag');
    expect(walkInBadge).toBeTruthy();
  }));

  it('(c2) pressing Enter on search input with new customer name selects it immediately', fakeAsync(() => {
    vi.spyOn(customersClient, 'getCustomers').mockReturnValue(
      of({ items: [], totalCount: 0, page: 1, pageSize: 10 })
    );
    const customerSelectedSpy = vi.spyOn(component.customerSelected, 'emit');

    component.onSearchFocus();
    component.searchControl.setValue('Sneha Rao');
    tick(300);
    fixture.detectChanges();

    const inputEl = fixture.nativeElement.querySelector('.customer-search-input') as HTMLInputElement;
    inputEl.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(customerSelectedSpy).toHaveBeenCalledWith(expect.objectContaining({
      id: '',
      name: 'Sneha Rao'
    }));
    expect(component.selectedCustomer().name).toBe('Sneha Rao');
    expect(component.isCustomWalkIn()).toBe(true);
  }));
  it('(c3) allows entering phone number for custom customer and propagates it', fakeAsync(() => {
    vi.spyOn(customersClient, 'getCustomers').mockReturnValue(
      of({ items: [], totalCount: 0, page: 1, pageSize: 10 })
    );
    const customerSelectedSpy = vi.spyOn(component.customerSelected, 'emit');

    component.onSearchFocus();
    component.searchControl.setValue('Kapil Dev');
    tick(300);
    fixture.detectChanges();

    const customOption = fixture.nativeElement.querySelector('.add-custom-option');
    customOption.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();

    // Phone input should now be visible in card
    const phoneInput = fixture.nativeElement.querySelector('.custom-phone-input') as HTMLInputElement;
    expect(phoneInput).toBeTruthy();

    // Type phone number
    phoneInput.value = '9876543210';
    phoneInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(component.selectedCustomer().phone).toBe('9876543210');
    expect(customerSelectedSpy).toHaveBeenLastCalledWith(expect.objectContaining({
      id: '',
      name: 'Kapil Dev',
      phone: '9876543210'
    }));
  }));

  describe('CreditLimitWarningDialogComponent Override Permission', () => {
    let dialogFixture: ComponentFixture<CreditLimitWarningDialogComponent>;
    const mockDialogData = {
      customerName: 'Anil Kumar',
      currentBalance: 8000,
      cartTotal: 3000,
      creditLimit: 10000
    };

    const mockDialogRef = {
      close: vi.fn(),
      afterClosed: () => of(undefined),
      isOpen: () => true
    };

    beforeEach(async () => {
      await TestBed.resetTestingModule();
      await TestBed.configureTestingModule({
        imports: [CreditLimitWarningDialogComponent],
        providers: [
          AuthSignalStore,
          { provide: PZ_DIALOG_DATA, useValue: mockDialogData },
          { provide: PzDialogRef, useValue: mockDialogRef }
        ]
      }).compileComponents();

      authStore = TestBed.inject(AuthSignalStore);
    });

    it('(d) the Override button is hidden for a user lacking sales:credit:override and shown for one who has it', () => {
      // 1. Without permission
      vi.spyOn(authStore, 'hasPermission').mockReturnValue(false);
      const fixtureWithoutPerm = TestBed.createComponent(CreditLimitWarningDialogComponent);
      fixtureWithoutPerm.detectChanges();

      const overrideBtnWithout = fixtureWithoutPerm.nativeElement.querySelector('.btn-override');
      expect(overrideBtnWithout).toBeNull();

      // 2. With permission
      vi.spyOn(authStore, 'hasPermission').mockReturnValue(true);
      const fixtureWithPerm = TestBed.createComponent(CreditLimitWarningDialogComponent);
      fixtureWithPerm.detectChanges();

      const overrideBtnWith = fixtureWithPerm.nativeElement.querySelector('.btn-override');
      expect(overrideBtnWith).toBeTruthy();
      expect(overrideBtnWith.textContent).toContain('Override');
    });
  });
});
