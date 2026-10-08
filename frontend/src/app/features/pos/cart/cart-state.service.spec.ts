import { TestBed } from '@angular/core/testing';
import { CartStateService } from './cart-state.service';
import type { CartProductInput } from './cart-state.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('CartStateService', () => {
  let service: CartStateService;
  let toastService: ToastService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [CartStateService, ToastService]
    });

    service = TestBed.inject(CartStateService);
    toastService = TestBed.inject(ToastService);
  });

  it('adding a plain accessory twice merges into one line with qty=2', () => {
    const accessory: CartProductInput = {
      productId: 'prod-acc-1',
      name: 'USB-C Cable',
      category: 'Accessories',
      sellPrice: 299,
      stockQty: 50
    };

    service.addItem(accessory);
    expect(service.items().length).toBe(1);
    expect(service.items()[0].qty).toBe(1);

    service.addItem(accessory);
    expect(service.items().length).toBe(1);
    expect(service.items()[0].qty).toBe(2);
    expect(service.subtotal()).toBe(598);
  });

  it('adding two DIFFERENT IMEIs of the same product creates two separate lines', () => {
    const phone: CartProductInput = {
      productId: 'prod-phone-1',
      name: 'Galaxy S24',
      category: 'Smartphones',
      sellPrice: 74999,
      stockQty: 5
    };

    service.addItem(phone, '358291048291041');
    service.addItem(phone, '358291048291042');

    expect(service.items().length).toBe(2);
    expect(service.items()[0].imei).toBe('358291048291041');
    expect(service.items()[0].qty).toBe(1);
    expect(service.items()[1].imei).toBe('358291048291042');
    expect(service.items()[1].qty).toBe(1);
    expect(service.totalQty()).toBe(2);
  });

  it('setting margin scheme on a non-UsedPhone line is rejected/ignored', () => {
    const newPhone: CartProductInput = {
      productId: 'prod-new-1',
      name: 'Brand New Phone',
      category: 'Smartphones',
      sellPrice: 50000,
      stockQty: 10
    };

    service.addItem(newPhone);
    const lineId = service.items()[0].id;
    expect(service.items()[0].useMarginScheme).toBe(false);

    service.setMarginScheme(lineId, true);
    // Non-UsedPhone line rejects/ignores margin scheme setting
    expect(service.items()[0].useMarginScheme).toBe(false);

    const usedPhone: CartProductInput = {
      productId: 'prod-used-1',
      name: 'Refurbished Phone',
      category: 'UsedPhone',
      sellPrice: 25000,
      stockQty: 1
    };
    service.addItem(usedPhone, '990011223344556');
    const usedLineId = service.items()[1].id;
    expect(service.items()[1].useMarginScheme).toBe(true);

    service.setMarginScheme(usedLineId, false);
    expect(service.items()[1].useMarginScheme).toBe(false);
  });

  it('removing a line works', () => {
    const item1: CartProductInput = {
      productId: 'p-1',
      name: 'Case',
      category: 'Accessories',
      sellPrice: 499,
      stockQty: 10
    };
    const item2: CartProductInput = {
      productId: 'p-2',
      name: 'Screen Guard',
      category: 'Accessories',
      sellPrice: 199,
      stockQty: 20
    };

    service.addItem(item1);
    service.addItem(item2);
    expect(service.items().length).toBe(2);

    const firstId = service.items()[0].id;
    service.removeItem(firstId);

    expect(service.items().length).toBe(1);
    expect(service.items()[0].name).toBe('Screen Guard');
  });

  it('the qty client-side soft-cap at reported stock level is enforced with toast warning but does not hard-block', () => {
    const toastSpy = vi.spyOn(toastService, 'warning');

    const lowStockItem: CartProductInput = {
      productId: 'p-limited',
      name: 'Limited Edition Headphones',
      category: 'Audio',
      sellPrice: 3999,
      stockQty: 2
    };

    // Add 1st item (stockQty = 2): no warning
    service.addItem(lowStockItem);
    expect(toastSpy).not.toHaveBeenCalled();
    expect(service.items()[0].qty).toBe(1);

    // Add 2nd item: reaches stockQty, no exceed warning
    service.addItem(lowStockItem);
    expect(toastSpy).not.toHaveBeenCalled();
    expect(service.items()[0].qty).toBe(2);

    // Add 3rd item: exceeds stockQty of 2 -> soft warning toast fired, but qty increments to 3 (does NOT hard-block)
    service.addItem(lowStockItem);
    expect(toastSpy).toHaveBeenCalledWith(
      expect.stringContaining('Requested quantity (3) exceeds reported stock (2)')
    );
    expect(service.items()[0].qty).toBe(3);

    // Increase qty via increaseQty method also triggers toast warning without hard blocking
    toastSpy.mockClear();
    service.increaseQty(service.items()[0].id);
    expect(toastSpy).toHaveBeenCalledWith(
      expect.stringContaining('Requested quantity (4) exceeds reported stock (2)')
    );
    expect(service.items()[0].qty).toBe(4);
  });
});
