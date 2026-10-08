import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { CustomersListComponent } from './customers-list.component';
import { CustomersClient } from '../../core/api/generated-client';
import type {
  PagedResultOfCustomerListItemDto,
  CreateCustomerCommand
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('CustomersListComponent', () => {
  let fixture: ComponentFixture<CustomersListComponent>;
  let component: CustomersListComponent;
  let customersClient: CustomersClient;

  const mockCustomersPage: PagedResultOfCustomerListItemDto = {
    items: [
      {
        id: 'cust-1',
        name: 'John Doe',
        phone: '9876543210',
        totalPurchases: 15000,
        currentCreditBalance: 2500
      },
      {
        id: 'cust-2',
        name: 'Jane Smith',
        phone: '9123456780',
        totalPurchases: 32000,
        currentCreditBalance: 0
      }
    ],
    totalCount: 2,
    page: 1,
    pageSize: 20
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CustomersListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        CustomersClient,
        ToastService
      ]
    }).compileComponents();

    customersClient = TestBed.inject(CustomersClient);
    vi.spyOn(customersClient, 'getCustomers').mockReturnValue(of(mockCustomersPage));
    vi.spyOn(customersClient, 'createCustomer').mockReturnValue(of('cust-3'));

    fixture = TestBed.createComponent(CustomersListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders customers list and triggers search query', async () => {
    const searchSpy = vi.spyOn(customersClient, 'getCustomers').mockReturnValue(of({
      items: [
        {
          id: 'cust-1',
          name: 'John Doe',
          phone: '9876543210',
          totalPurchases: 15000,
          currentCreditBalance: 2500
        }
      ],
      totalCount: 1,
      page: 1,
      pageSize: 20
    }));

    await component.onSearch('John');
    fixture.detectChanges();

    expect(searchSpy).toHaveBeenCalledWith('John', 1, 20);
    expect(component.customers().length).toBe(1);
    expect(component.customers()[0].name).toBe('John Doe');
  });

  it('submits create customer command and refreshes list', async () => {
    const createSpy = vi.spyOn(customersClient, 'createCustomer').mockReturnValue(of('cust-new'));
    const listSpy = vi.spyOn(customersClient, 'getCustomers').mockReturnValue(of(mockCustomersPage));

    component.openCreateModal();
    fixture.detectChanges();
    expect(component.isCreateModalOpen()).toBe(true);

    component.newName.set('Alice Wonderland');
    component.newPhone.set('9988776655');
    component.newGstin.set('27ABCDE1234F1Z5');
    fixture.detectChanges();

    await component.submitCreateCustomer();
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalledTimes(1);
    const sentCommand = createSpy.mock.calls[0][0] as CreateCustomerCommand;
    expect(sentCommand.name).toBe('Alice Wonderland');
    expect(sentCommand.phone).toBe('9988776655');
    expect(sentCommand.gstin).toBe('27ABCDE1234F1Z5');

    expect(component.isCreateModalOpen()).toBe(false);
    expect(listSpy).toHaveBeenCalled();
  });
});
