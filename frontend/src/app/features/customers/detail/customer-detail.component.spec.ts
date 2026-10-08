import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { CustomerDetailComponent } from './customer-detail.component';
import { CustomersClient } from '../../../core/api/generated-client';
import type {
  CustomerDetailDto,
  CreditBalanceDto,
  PagedResultOfSaleSummaryDto,
  PagedResultOfCustomerLedgerEntryDto
} from '../../../core/api/generated-client';
import { AuthSignalStore } from '../../../core/auth/auth-signal-store';
import { ToastService } from '../../../shared/services/toast.service';

describe('CustomerDetailComponent', () => {
  let fixture: ComponentFixture<CustomerDetailComponent>;
  let component: CustomerDetailComponent;
  let customersClient: CustomersClient;
  let authStore: AuthSignalStore;

  const mockCustomer: CustomerDetailDto = {
    id: 'cust-123',
    name: 'Alice Johnson',
    phone: '9876543210',
    gstin: '29ABCDE1234F2Z5',
    totalPurchases: 54000,
    creditLimit: 10000,
    currentCreditBalance: 3500,
    createdAt: new Date('2026-01-15T10:00:00Z')
  };

  const mockCreditBalance: CreditBalanceDto = {
    customerId: 'cust-123',
    consolidatedBalance: 3500,
    creditLimit: 10000,
    availableCredit: 6500,
    isOverLimit: false
  };

  const mockSales: PagedResultOfSaleSummaryDto = {
    items: [
      {
        id: 'sale-001',
        invoiceNumber: 'INV-2026-042',
        saleDate: new Date('2026-03-01T14:30:00Z'),
        grandTotal: 18000,
        status: 'Completed'
      }
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50
  };

  const mockLedger: PagedResultOfCustomerLedgerEntryDto = {
    items: [
      {
        id: 1,
        entryType: 'Sale',
        debit: 18000,
        credit: 0,
        branchId: 'branch-1',
        branchName: 'Main Store',
        entryDate: new Date('2026-03-01T14:30:00Z')
      }
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomerDetailComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'id' ? 'cust-123' : null)
              }
            }
          }
        },
        CustomersClient,
        AuthSignalStore,
        ToastService
      ]
    }).compileComponents();

    customersClient = TestBed.inject(CustomersClient);
    authStore = TestBed.inject(AuthSignalStore);

    vi.spyOn(customersClient, 'getCustomerById').mockReturnValue(of(mockCustomer));
    vi.spyOn(customersClient, 'getCreditBalance').mockReturnValue(of(mockCreditBalance));
    vi.spyOn(customersClient, 'getCustomerSales').mockReturnValue(of(mockSales));
    vi.spyOn(customersClient, 'getCustomerLedger').mockReturnValue(of(mockLedger));

    fixture = TestBed.createComponent(CustomerDetailComponent);
    component = fixture.componentInstance;
  });

  it('credit-limit edit control is hidden without settings:manage even when profile-edit control (pos:checkout) is visible', async () => {
    authStore.setAuthenticated({
      accessToken: 'token-cashier',
      expiresAt: '2026-10-08T18:00:00Z',
      userId: 'cashier-1',
      displayName: 'Cashier Representative',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['pos:checkout']
    });

    fixture.detectChanges();
    await component.loadAllData('cust-123');
    fixture.detectChanges();

    const nativeEl = fixture.nativeElement as HTMLElement;

    // Profile edit control (needing only pos:checkout) must be VISIBLE
    const profileEditBtn = nativeEl.querySelector('.btn-edit-profile');
    expect(profileEditBtn).toBeTruthy();

    // Credit limit edit control (needing settings:manage) must be HIDDEN
    const creditLimitEditBtn = nativeEl.querySelector('.btn-edit-credit-limit');
    expect(creditLimitEditBtn).toBeNull();
  });

  it('credit-limit edit control is visible when user has settings:manage permission', async () => {
    authStore.setAuthenticated({
      accessToken: 'token-admin',
      expiresAt: '2026-10-08T18:00:00Z',
      userId: 'admin-1',
      displayName: 'Store Manager',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['pos:checkout', 'settings:manage']
    });

    fixture.detectChanges();
    await component.loadAllData('cust-123');
    fixture.detectChanges();

    const nativeEl = fixture.nativeElement as HTMLElement;

    const profileEditBtn = nativeEl.querySelector('.btn-edit-profile');
    expect(profileEditBtn).toBeTruthy();

    const creditLimitEditBtn = nativeEl.querySelector('.btn-edit-credit-limit');
    expect(creditLimitEditBtn).toBeTruthy();
  });
});
