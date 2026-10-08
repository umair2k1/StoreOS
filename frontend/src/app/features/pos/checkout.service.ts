import { Injectable, signal, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PosClient, SalesClient } from '../../core/api/generated-client';
import type {
  CheckoutCommand,
  CheckoutCartLine,
  CheckoutPayment,
  CheckoutFinanceDetails,
  CustomerListItemDto,
  FileResponse
} from '../../core/api/generated-client';
import { CartStateService } from './cart/cart-state.service';
import { ToastService } from '../../shared/services/toast.service';

export interface CheckoutOptions {
  customer?: CustomerListItemDto | null;
  payments: CheckoutPayment[];
  finance?: CheckoutFinanceDetails | null;
  invoiceDiscount?: number;
  description?: string;
}

export interface CheckoutResult {
  success: boolean;
  saleId?: string;
  invoiceNumber?: string;
  printSucceeded?: boolean;
  error?: string;
}

interface SaleCreatedResponse {
  saleId?: string;
  invoiceNumber?: string;
  grandTotal?: number;
  amountPaid?: number;
  balance?: number;
}

@Injectable({
  providedIn: 'root'
})
export class CheckoutService {
  private readonly posClient = inject(PosClient);
  private readonly salesClient = inject(SalesClient);
  private readonly cartState = inject(CartStateService);
  private readonly toastService = inject(ToastService);

  readonly isSubmitting = signal(false);
  readonly lastSaleId = signal<string | null>(null);
  readonly lastInvoiceNumber = signal<string | null>(null);

  async checkout(options: CheckoutOptions): Promise<CheckoutResult> {
    const items = this.cartState.items();
    if (items.length === 0) {
      this.toastService.warning('Cannot checkout with an empty cart.');
      return { success: false, error: 'empty_cart' };
    }

    const lines: CheckoutCartLine[] = items.map(item => ({
      productId: item.productId,
      qty: item.qty,
      imei: item.imei,
      useMarginScheme: item.useMarginScheme,
      isGift: item.isGift,
      overridePricePerUnit: item.overridePricePerUnit
    }));

    const customerId = options.customer?.id ? options.customer.id : undefined;
    const walkInName = !customerId ? (options.customer?.name || 'Walk-in Customer') : undefined;

    const command: CheckoutCommand = {
      lines,
      customerId,
      walkInCustomerName: walkInName,
      invoiceDiscount: options.invoiceDiscount ?? 0,
      payments: options.payments,
      finance: options.finance || undefined,
      description: options.description
    };

    this.isSubmitting.set(true);

    try {
      const response = await firstValueFrom(this.posClient.checkout(command));
      this.isSubmitting.set(false);

      // Parse sale identification from response
      const saleData = await this.parseSaleResponse(response);
      const saleId = saleData.saleId || '';
      const invoiceNumber = saleData.invoiceNumber || saleId || 'Invoice';

      this.lastSaleId.set(saleId);
      this.lastInvoiceNumber.set(invoiceNumber);

      // INVARIANT: Checkout succeeded server-side -> clear cart immediately
      this.cartState.clear();

      // Trigger post-checkout PDF print flow
      const printSuccess = await this.executePrintFlow(saleId, invoiceNumber);

      return {
        success: true,
        saleId,
        invoiceNumber,
        printSucceeded: printSuccess
      };
    } catch (err: unknown) {
      this.isSubmitting.set(false);

      const errorMessage = this.extractErrorMessage(err);
      this.toastService.error(`Checkout failed: ${errorMessage}`);

      // INVARIANT: On checkout error, the cart is NEVER cleared so the user can fix and retry
      return {
        success: false,
        error: errorMessage
      };
    }
  }

  async reprintInvoice(saleId?: string): Promise<boolean> {
    const targetId = saleId || this.lastSaleId();
    if (!targetId) {
      this.toastService.warning('No recent sale found to reprint.');
      return false;
    }

    try {
      const response = await firstValueFrom(this.salesClient.getInvoicePdf(targetId));
      this.openPdfBlob(response.data);
      this.toastService.success('Invoice reprint sent.');
      return true;
    } catch {
      this.toastService.warning('Reprint failed. Please check printer connection and try again.');
      return false;
    }
  }

  private async executePrintFlow(saleId: string, invoiceNumber: string): Promise<boolean> {
    if (!saleId) {
      this.toastService.success(`Sale completed (${invoiceNumber})`);
      return true;
    }

    try {
      const pdfResponse = await firstValueFrom(this.salesClient.getInvoicePdf(saleId));
      this.openPdfBlob(pdfResponse.data);
      this.toastService.success(`Sale completed (${invoiceNumber})`);
      return true;
    } catch {
      // INVARIANT (Requirements §40 & Plan):
      // If PDF fetch/print fails AFTER successful checkout, the sale is ALREADY committed.
      // Must show success toast stating sale succeeded with printing failed, NEVER imply rollback.
      this.toastService.warning(
        `Sale completed (${invoiceNumber}) — printing failed. Reprint available.`
      );
      return false;
    }
  }

  private openPdfBlob(blob: Blob): void {
    try {
      const url = window.URL.createObjectURL(blob);
      const printWindow = window.open(url, '_blank');
      if (printWindow) {
        setTimeout(() => {
          try {
            printWindow.print();
          } catch {
            // Browser blocked automated print dialog; window opened as fallback
          }
        }, 500);
      }
    } catch {
      // Fallback if window creation fails in headless / test environment
    }
  }

  private async parseSaleResponse(response: FileResponse): Promise<SaleCreatedResponse> {
    try {
      if (response && response.data && typeof response.data.text === 'function') {
        const text = await response.data.text();
        if (text && text.trim().length > 0) {
          return JSON.parse(text) as SaleCreatedResponse;
        }
      }
    } catch {
      // Ignored: fallback to empty response
    }
    return {};
  }

  private extractErrorMessage(err: unknown): string {
    if (err && typeof err === 'object') {
      const withMsg = err as { message?: string; response?: string; status?: number };
      if (withMsg.message) {
        return withMsg.message;
      }
      if (withMsg.response) {
        try {
          const parsed = JSON.parse(withMsg.response) as { message?: string; detail?: string };
          if (parsed.message) {
            return parsed.message;
          }
          if (parsed.detail) {
            return parsed.detail;
          }
        } catch {
          return withMsg.response;
        }
      }
    }
    return 'An unexpected error occurred during checkout.';
  }
}
