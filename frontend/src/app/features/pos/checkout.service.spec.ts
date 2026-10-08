import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of, throwError } from 'rxjs';
import { CheckoutService } from './checkout.service';
import type { CheckoutOptions } from './checkout.service';
import { CartStateService } from './cart/cart-state.service';
import { ToastService } from '../../shared/services/toast.service';
import { PosClient, SalesClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';

describe('CheckoutService', () => {
  let service: CheckoutService;
  let posClient: PosClient;
  let salesClient: SalesClient;
  let cartState: CartStateService;
  let toastService: ToastService;

  const mockSuccessCheckoutResponse: FileResponse = {
    data: new Blob([
      JSON.stringify({
        saleId: 'sale-uuid-101',
        invoiceNumber: 'INV-2026-001',
        grandTotal: 1500,
        amountPaid: 1500,
        balance: 0
      })
    ]),
    status: 201,
    headers: {}
  };

  const mockPdfResponse: FileResponse = {
    data: new Blob(['%PDF-1.4 mock content'], { type: 'application/pdf' }),
    status: 200,
    headers: {}
  };

  const sampleCheckoutOptions: CheckoutOptions = {
    customer: { id: 'cust-1', name: 'Rohan Sharma' },
    payments: [{ paymentMode: 'cash', amount: 1500 }],
    invoiceDiscount: 0
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        CheckoutService,
        CartStateService,
        ToastService,
        PosClient,
        SalesClient
      ]
    });

    service = TestBed.inject(CheckoutService);
    posClient = TestBed.inject(PosClient);
    salesClient = TestBed.inject(SalesClient);
    cartState = TestBed.inject(CartStateService);
    toastService = TestBed.inject(ToastService);

    cartState.clear();
  });

  it('(a) a successful checkout response clears the cart and shows a success toast', async () => {
    // Add product to cart
    cartState.addItem({
      productId: 'prod-1',
      name: 'Wireless Charger',
      sellPrice: 1500,
      stockQty: 5
    });
    expect(cartState.items().length).toBe(1);

    vi.spyOn(posClient, 'checkout').mockReturnValue(of(mockSuccessCheckoutResponse as unknown as void));
    vi.spyOn(salesClient, 'getInvoicePdf').mockReturnValue(of(mockPdfResponse));
    const toastSuccessSpy = vi.spyOn(toastService, 'success');

    const result = await service.checkout(sampleCheckoutOptions);

    expect(result.success).toBe(true);
    expect(result.invoiceNumber).toBe('INV-2026-001');
    expect(result.printSucceeded).toBe(true);

    // Cart is cleared upon successful sale
    expect(cartState.items().length).toBe(0);

    // Success toast displayed
    expect(toastSuccessSpy).toHaveBeenCalledWith(
      expect.stringContaining('Sale completed (INV-2026-001)')
    );
  });

  it('(b) a successful checkout response FOLLOWED BY a failed PDF fetch still shows the sale-succeeded toast with printing-failed notice', async () => {
    cartState.addItem({
      productId: 'prod-2',
      name: 'Power Bank',
      sellPrice: 1500,
      stockQty: 10
    });
    expect(cartState.items().length).toBe(1);

    // Checkout succeeds
    vi.spyOn(posClient, 'checkout').mockReturnValue(of(mockSuccessCheckoutResponse as unknown as void));
    // BUT PDF fetch/print fails
    vi.spyOn(salesClient, 'getInvoicePdf').mockReturnValue(
      throwError(() => new Error('Printer connection timed out'))
    );

    const toastWarningSpy = vi.spyOn(toastService, 'warning');
    const toastErrorSpy = vi.spyOn(toastService, 'error');

    const result = await service.checkout(sampleCheckoutOptions);

    // INVARIANT: Sale was already committed!
    expect(result.success).toBe(true);
    expect(result.printSucceeded).toBe(false);

    // Cart is still cleared because the sale succeeded
    expect(cartState.items().length).toBe(0);

    // Crucial: Must NEVER show an error framing that the sale failed or was rolled back
    expect(toastErrorSpy).not.toHaveBeenCalled();

    // Must show toast stating the sale succeeded with printing failed & reprint available
    expect(toastWarningSpy).toHaveBeenCalledWith(
      expect.stringContaining('Sale completed (INV-2026-001) — printing failed. Reprint available.')
    );
  });

  it('(c) a genuine checkout FAILURE shows an error toast and does NOT clear the cart', async () => {
    cartState.addItem({
      productId: 'prod-3',
      name: 'Ultra Wide Monitor',
      sellPrice: 35000,
      stockQty: 0
    });
    expect(cartState.items().length).toBe(1);

    // Checkout fails server-side (e.g. 422 insufficient stock)
    vi.spyOn(posClient, 'checkout').mockReturnValue(
      throwError(() => ({
        status: 422,
        message: 'Insufficient stock for product prod-3'
      }))
    );

    const toastErrorSpy = vi.spyOn(toastService, 'error');
    const toastSuccessSpy = vi.spyOn(toastService, 'success');

    const result = await service.checkout(sampleCheckoutOptions);

    expect(result.success).toBe(false);
    expect(result.error).toContain('Insufficient stock');

    // INVARIANT: Cart is NOT cleared on genuine checkout failure, allowing user to edit and retry
    expect(cartState.items().length).toBe(1);

    // Error toast shown
    expect(toastErrorSpy).toHaveBeenCalledWith(
      expect.stringContaining('Checkout failed: Insufficient stock')
    );
    expect(toastSuccessSpy).not.toHaveBeenCalled();
  });
});
