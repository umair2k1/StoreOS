import { ComponentFixture, TestBed } from '@angular/core/testing';
import { CartComponent } from './cart.component';
import { CartStateService } from './cart-state.service';
import type { CartProductInput } from './cart-state.service';
import { ToastService } from '../../../shared/services/toast.service';
import { AuthSignalStore } from '../../../core/auth/auth-signal-store';

describe('CartComponent', () => {
  let cartState: CartStateService;
  let authStore: AuthSignalStore;

  const testProduct: CartProductInput = {
    productId: 'prod-item-1',
    name: 'Wireless Mouse',
    category: 'Accessories',
    sellPrice: 1200,
    stockQty: 10
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CartComponent],
      providers: [CartStateService, ToastService, AuthSignalStore]
    });

    cartState = TestBed.inject(CartStateService);
    authStore = TestBed.inject(AuthSignalStore);
    cartState.clear();
  });

  it('gift and override fields are hidden without sales:discount:apply and shown with it', () => {
    // Add product to cart
    cartState.addItem(testProduct);

    // 1. Without sales:discount:apply permission
    vi.spyOn(authStore, 'hasPermission').mockReturnValue(false);
    const fixtureWithoutPerm = TestBed.createComponent(CartComponent);
    fixtureWithoutPerm.detectChanges();

    const giftToggleWithout = fixtureWithoutPerm.nativeElement.querySelector('.gift-toggle');
    const overrideInputWithout = fixtureWithoutPerm.nativeElement.querySelector('.override-input-wrapper');
    expect(giftToggleWithout).toBeNull();
    expect(overrideInputWithout).toBeNull();

    // 2. With sales:discount:apply permission
    vi.spyOn(authStore, 'hasPermission').mockImplementation((perm: string) => perm === 'sales:discount:apply');
    const fixtureWithPerm = TestBed.createComponent(CartComponent);
    fixtureWithPerm.detectChanges();

    const giftToggleWith = fixtureWithPerm.nativeElement.querySelector('.gift-toggle');
    const overrideInputWith = fixtureWithPerm.nativeElement.querySelector('.override-input-wrapper');
    expect(giftToggleWith).toBeTruthy();
    expect(overrideInputWith).toBeTruthy();
  });

  it('wires +, -, and Del hotkeys to currently-selected cart row', () => {
    cartState.addItem(testProduct);
    const item = cartState.items()[0];
    cartState.selectItem(item.id);

    const fixture = TestBed.createComponent(CartComponent);
    fixture.detectChanges();

    // Press '+' -> increases quantity to 2
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '+', cancelable: true }));
    expect(cartState.items()[0].qty).toBe(2);

    // Press '-' -> decreases quantity to 1
    window.dispatchEvent(new KeyboardEvent('keydown', { key: '-', cancelable: true }));
    expect(cartState.items()[0].qty).toBe(1);

    // Press 'Delete' -> removes row
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Delete', cancelable: true }));
    expect(cartState.items().length).toBe(0);
  });
});
