import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, Params } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { SalesHistoryComponent } from './sales-history.component';
import { SalesClient } from '../../core/api/generated-client';
import type { PagedResultOfSaleSummaryDto } from '../../core/api/generated-client';

describe('SalesHistoryComponent', () => {
  let fixture: ComponentFixture<SalesHistoryComponent>;
  let component: SalesHistoryComponent;
  let salesClient: SalesClient;
  let router: Router;
  let queryParams$: BehaviorSubject<Params>;

  const mockResult: PagedResultOfSaleSummaryDto = {
    items: [
      {
        id: 'sale-001',
        invoiceNumber: 'INV-2026-0001',
        saleDate: new Date('2026-10-08T10:00:00Z'),
        customerName: 'Aarav Sharma',
        grandTotal: 15000,
        amountPaid: 15000,
        balance: 0,
        status: 'completed',
        invoiceType: 'tax_invoice',
        paymentMode: 'upi',
        isFinanced: false
      }
    ],
    totalCount: 1,
    page: 1,
    pageSize: 50
  };

  beforeEach(async () => {
    queryParams$ = new BehaviorSubject<Params>({});

    const routeStub = {
      queryParams: queryParams$.asObservable(),
      snapshot: {
        get queryParams() {
          return queryParams$.value;
        }
      }
    };

    await TestBed.configureTestingModule({
      imports: [SalesHistoryComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        SalesClient,
        { provide: ActivatedRoute, useValue: routeStub }
      ]
    }).compileComponents();

    salesClient = TestBed.inject(SalesClient);
    router = TestBed.inject(Router);

    vi.spyOn(salesClient, 'getSales').mockReturnValue(of(mockResult));
    vi.spyOn(router, 'navigate').mockImplementation(async (_commands, extras) => {
      if (extras?.queryParams) {
        queryParams$.next(extras.queryParams);
      }
      return true;
    });

    fixture = TestBed.createComponent(SalesHistoryComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('fetches sales on init with default parameters', () => {
    expect(salesClient.getSales).toHaveBeenCalledWith(
      undefined, // search
      undefined, // dateFilter
      undefined, // from
      undefined, // to
      undefined, // status
      undefined, // paymentMode
      undefined, // invoiceType
      undefined, // paidOnly
      undefined, // dueOnly
      undefined, // financedOnly
      undefined, // category
      undefined, // minAmount
      undefined, // maxAmount
      'SaleDate',
      'desc',
      1,
      50
    );

    expect(component.sales().length).toBe(1);
    expect(component.sales()[0].invoiceNumber).toBe('INV-2026-0001');
  });

  it('(a) each filter control correctly updates the query params and triggers a re-fetch with right parameters', () => {
    // 1. Search update
    component.updateParam('search', 'Samsung');
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ search: 'Samsung' })
    }));
    expect(salesClient.getSales).toHaveBeenCalledWith(
      'Samsung',
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      'SaleDate',
      'desc',
      1,
      50
    );

    // 2. Status update
    component.updateParam('status', 'completed');
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ status: 'completed' })
    }));
    expect(salesClient.getSales).toHaveBeenCalledWith(
      'Samsung',
      undefined,
      undefined,
      undefined,
      'completed',
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      'SaleDate',
      'desc',
      1,
      50
    );

    // 3. Paid Only toggle
    component.updateParam('paidOnly', true);
    expect(salesClient.getSales).toHaveBeenCalledWith(
      'Samsung',
      undefined,
      undefined,
      undefined,
      'completed',
      undefined,
      undefined,
      true,
      undefined,
      undefined,
      undefined,
      undefined,
      undefined,
      'SaleDate',
      'desc',
      1,
      50
    );

    // 4. Amount range
    component.updateParam('minAmount', 1000);
    component.updateParam('maxAmount', 50000);
    expect(salesClient.getSales).toHaveBeenCalledWith(
      'Samsung',
      undefined,
      undefined,
      undefined,
      'completed',
      undefined,
      undefined,
      true,
      undefined,
      undefined,
      undefined,
      1000,
      50000,
      'SaleDate',
      'desc',
      1,
      50
    );
  });

  it('(b) restoring the component with pre-set query params correctly initializes filter control values', async () => {
    // Re-initialize with query params
    const initialParams: Params = {
      search: 'Invoice-99',
      dateFilter: 'this_month',
      status: 'voided',
      paymentMode: 'card',
      invoiceType: 'estimate',
      paidOnly: 'true',
      dueOnly: 'false',
      financedOnly: 'true',
      category: 'Smartphones',
      minAmount: '500',
      maxAmount: '12000',
      sortBy: 'GrandTotal',
      sortDirection: 'asc',
      page: '2',
      pageSize: '25'
    };

    queryParams$.next(initialParams);
    fixture.detectChanges();

    expect(component.search()).toBe('Invoice-99');
    expect(component.dateFilter()).toBe('this_month');
    expect(component.status()).toBe('voided');
    expect(component.paymentMode()).toBe('card');
    expect(component.invoiceType()).toBe('estimate');
    expect(component.paidOnly()).toBe(true);
    expect(component.dueOnly()).toBe(false);
    expect(component.financedOnly()).toBe(true);
    expect(component.category()).toBe('Smartphones');
    expect(component.minAmount()).toBe(500);
    expect(component.maxAmount()).toBe(12000);
    expect(component.sortBy()).toBe('GrandTotal');
    expect(component.sortDirection()).toBe('asc');
    expect(component.page()).toBe(2);
    expect(component.pageSize()).toBe(25);
  });

  it('(c) sortable column headers toggle ascending/descending correctly', () => {
    // Sort by GrandTotal ascending
    component.onSort({ column: 'GrandTotal', direction: 'asc' });

    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({
        sortBy: 'GrandTotal',
        sortDirection: 'asc',
        page: 1
      })
    }));

    expect(component.sortBy()).toBe('GrandTotal');
    expect(component.sortDirection()).toBe('asc');

    // Sort by GrandTotal descending
    component.onSort({ column: 'GrandTotal', direction: 'desc' });

    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({
        sortBy: 'GrandTotal',
        sortDirection: 'desc',
        page: 1
      })
    }));

    expect(component.sortBy()).toBe('GrandTotal');
    expect(component.sortDirection()).toBe('desc');
  });

  it('navigates to sales detail on row click', () => {
    component.onRowClick({ id: 'sale-123', invoiceType: 'tax_invoice' });
    expect(router.navigate).toHaveBeenCalledWith(['/sales', 'sale-123']);

    component.onRowClick({ id: 'est-456', invoiceType: 'estimate' });
    expect(router.navigate).toHaveBeenCalledWith(['/sales/estimates', 'est-456']);
  });
});
