import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, Params } from '@angular/router';
import { BehaviorSubject, of } from 'rxjs';
import { InventoryListComponent } from './inventory-list.component';
import { InventoryClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';

describe('InventoryListComponent', () => {
  let fixture: ComponentFixture<InventoryListComponent>;
  let component: InventoryListComponent;
  let inventoryClient: InventoryClient;
  let router: Router;
  let queryParams$: BehaviorSubject<Params>;

  const mockInventoryItems = [
    {
      productId: 'prod-001',
      branchId: 'branch-1',
      productName: 'iPhone 15 Pro',
      category: 'Smartphones',
      stockQty: 10,
      costPrice: 95000,
      effectiveSellPrice: 119900,
      gstRate: 18,
      isMarginScheme: false,
      hsnCode: '8517',
      stockStatus: 'In Stock'
    },
    {
      productId: 'prod-002',
      branchId: 'branch-1',
      productName: 'OnePlus 12',
      category: 'Smartphones',
      stockQty: 2,
      costPrice: 52000,
      effectiveSellPrice: 64999,
      gstRate: 18,
      isMarginScheme: false,
      hsnCode: '8517',
      stockStatus: 'Low Stock'
    },
    {
      productId: 'prod-003',
      branchId: 'branch-1',
      productName: 'Pixel 8',
      category: 'Smartphones',
      stockQty: 0,
      costPrice: 60000,
      effectiveSellPrice: 75999,
      gstRate: 18,
      isMarginScheme: false,
      hsnCode: '8517',
      stockStatus: 'Out of Stock'
    }
  ];

  const mockResponsePayload = JSON.stringify({
    items: mockInventoryItems,
    totalCount: 3,
    page: 1,
    pageSize: 50
  });

  const mockFileResponse: FileResponse = {
    data: new Blob([mockResponsePayload], { type: 'application/json' }),
    status: 200,
    fileName: 'inventory.json',
    headers: {}
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
      imports: [InventoryListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        InventoryClient,
        { provide: ActivatedRoute, useValue: routeStub }
      ]
    }).compileComponents();

    inventoryClient = TestBed.inject(InventoryClient);
    router = TestBed.inject(Router);

    vi.spyOn(inventoryClient, 'list').mockReturnValue(of(mockFileResponse));
    vi.spyOn(inventoryClient, 'categories').mockReturnValue(of(['Smartphones', 'Tablets']));
    vi.spyOn(router, 'navigate').mockImplementation(async (_commands, extras) => {
      if (extras?.queryParams) {
        queryParams$.next(extras.queryParams);
      }
      return true;
    });

    fixture = TestBed.createComponent(InventoryListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('fetches and displays inventory items on init with visual stock status badges', async () => {
    await fixture.whenStable();
    fixture.detectChanges();

    expect(inventoryClient.list).toHaveBeenCalled();
    expect(component.items().length).toBe(3);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('iPhone 15 Pro');
    expect(el.textContent).toContain('OnePlus 12');
    expect(el.textContent).toContain('Pixel 8');

    // Visual stock availability badges check
    const inStockBadge = el.querySelector('.badge-stock.in-stock');
    const lowStockBadge = el.querySelector('.badge-stock.low-stock');
    const outOfStockBadge = el.querySelector('.badge-stock.out-of-stock');

    expect(inStockBadge).not.toBeNull();
    expect(lowStockBadge).not.toBeNull();
    expect(outOfStockBadge).not.toBeNull();
  });

  it('filters correctly update query parameters and trigger re-fetch', async () => {
    // 1. Search update
    component.updateParam('search', 'iPhone');
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ search: 'iPhone' })
    }));

    // 2. Category update
    component.updateParam('category', 'Smartphones');
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ category: 'Smartphones' })
    }));

    // 3. Stock Status update
    component.updateParam('stockStatus', 'low_stock');
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ stockStatus: 'low_stock' })
    }));

    // 4. Margin scheme update
    component.updateParam('isMarginScheme', 'true');
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ isMarginScheme: 'true' })
    }));
  });

  it('renders category dropdown with existing categories and updates parameter on selection', async () => {
    await fixture.whenStable();
    fixture.detectChanges();

    const selectEl = fixture.nativeElement.querySelector('#invCategory') as HTMLSelectElement;
    expect(selectEl).not.toBeNull();
    const options = Array.from(selectEl.options).map(o => o.text.trim());
    expect(options).toContain('All Categories');
    expect(options).toContain('Smartphones');
    expect(options).toContain('Tablets');

    selectEl.value = 'Smartphones';
    selectEl.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ category: 'Smartphones' })
    }));
  });

  it('sort and pagination update query parameters correctly', () => {
    // Sort
    component.onSort({ column: 'StockQty', direction: 'desc' });
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ sortBy: 'StockQty', sortDirection: 'desc' })
    }));
    // Page change
    component.totalCount.set(100);
    component.onPageChange(2);
    expect(router.navigate).toHaveBeenCalledWith([], expect.objectContaining({
      queryParams: expect.objectContaining({ page: 2 })
    }));
  });

  it('selecting multiple rows and triggering bulk export action navigates with pre-selected product ids', async () => {
    await fixture.whenStable();
    fixture.detectChanges();

    expect(component.hasSelection()).toBe(false);

    // Select row 1 and row 2
    component.onSelectRow({ row: mockInventoryItems[0], selected: true });
    component.onSelectRow({ row: mockInventoryItems[1], selected: true });
    fixture.detectChanges();

    expect(component.hasSelection()).toBe(true);
    expect(component.selectedCount()).toBe(2);
    expect(component.selectedProductIds().has('prod-001')).toBe(true);
    expect(component.selectedProductIds().has('prod-002')).toBe(true);

    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.bulk-action-bar')).not.toBeNull();
    expect(el.textContent).toContain('2 product(s) selected');

    // Trigger bulk export
    component.exportSelected();

    expect(router.navigate).toHaveBeenCalledWith(
      ['/inventory/export'],
      expect.objectContaining({
        queryParams: expect.objectContaining({
          productIds: expect.stringMatching(/prod-001.*prod-002|prod-002.*prod-001/)
        })
      })
    );
  });
});
