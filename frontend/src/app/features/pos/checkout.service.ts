import { Injectable, signal, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { PosClient, SalesClient, CustomersClient } from '../../core/api/generated-client';
import type {
  CheckoutCommand,
  CheckoutCartLine,
  CheckoutPayment,
  CheckoutFinanceDetails,
  CustomerListItemDto,
  FileResponse,
  CreateEstimateBody
} from '../../core/api/generated-client';
import { CartStateService } from './cart/cart-state.service';
import { ToastService } from '../../shared/services/toast.service';

export interface CheckoutOptions {
  customer?: CustomerListItemDto | null;
  payments: CheckoutPayment[];
  finance?: CheckoutFinanceDetails | null;
  invoiceDiscount?: number;
  description?: string;
  autoPrint?: boolean;
}
export interface EstimateOptions {
  customer?: CustomerListItemDto | null;
  payments?: CheckoutPayment[];
  invoiceDiscount?: number;
  description?: string;
}

export interface EstimateResult {
  success: boolean;
  estimateId?: string;
  invoiceNumber?: string;
  error?: string;
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
  private readonly customersClient = inject(CustomersClient);
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

    const effectivePayments: CheckoutPayment[] = options.payments && options.payments.length > 0
      ? options.payments
      : [{ paymentMode: 'Cash', amount: options.finance ? (options.finance.downPayment || 0) : this.cartState.subtotal() }];

    const command: CheckoutCommand = {
      lines,
      customerId,
      walkInCustomerName: walkInName,
      invoiceDiscount: options.invoiceDiscount ?? 0,
      invoiceType: 'TaxInvoice',
      payments: effectivePayments,
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

      // Save new customer created on the go into customer directory
      if (!options.customer?.id && options.customer?.name && options.customer.name.trim() !== 'Walk-in Customer') {
        try {
          const newName = options.customer.name.trim();
          const newPhone = options.customer.phone?.trim() || undefined;
          await firstValueFrom(this.customersClient.createCustomer({
            name: newName,
            phone: newPhone
          }));
          this.toastService.success(`Customer "${newName}" added to customer directory.`);
        } catch {
          // Non-blocking: Sale is already completed; do not fail checkout if customer directory insert errors
        }
      }

      // Trigger post-checkout PDF print flow if autoPrint is enabled
      if (options.autoPrint) {
        const printSuccess = await this.executePrintFlow(saleId, invoiceNumber);
        return {
          success: true,
          saleId,
          invoiceNumber,
          printSucceeded: printSuccess
        };
      }

      this.toastService.success(`Sale completed (${invoiceNumber})`);
      return {
        success: true,
        saleId,
        invoiceNumber
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

  async saveEstimate(options: EstimateOptions): Promise<EstimateResult> {
    const items = this.cartState.items();
    if (items.length === 0) {
      this.toastService.warning('Cannot save estimate with an empty cart.');
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

    const customerId = options.customer?.id;
    const walkInCustomerName = !customerId
      ? (options.customer?.name || 'Walk-in Customer')
      : undefined;

    const body: CreateEstimateBody = {
      lines,
      customerId,
      walkInCustomerName,
      invoiceDiscount: options.invoiceDiscount ?? 0,
      payments: options.payments && options.payments.length > 0 ? options.payments : [],
      description: options.description
    };

    this.isSubmitting.set(true);

    try {
      const response = await firstValueFrom(this.posClient.createEstimate(body));
      const parsedData = await this.parseSaleResponse(response);
      const estimateId = parsedData.saleId;
      const invoiceNumber = parsedData.invoiceNumber || 'Estimate';

      // Estimate saved: clear cart
      this.cartState.clear();
      this.toastService.success(`Estimate saved (${invoiceNumber})`);

      return {
        success: true,
        estimateId,
        invoiceNumber
      };
    } catch (err: unknown) {
      const errorMessage = this.extractErrorMessage(err);
      this.toastService.error(`Failed to save estimate: ${errorMessage}`);
      return {
        success: false,
        error: errorMessage
      };
    } finally {
      this.isSubmitting.set(false);
    }
  }
  async printInvoice(saleId?: string, isReprint = false): Promise<boolean> {
    const targetId = saleId || this.lastSaleId();
    if (!targetId) {
      this.toastService.warning(isReprint ? 'No recent sale found to reprint.' : 'No recent sale found to print.');
      return false;
    }

    try {
      const response = await firstValueFrom(this.salesClient.getInvoicePdf(targetId));
      this.openPdfBlob(response.data);
      this.toastService.success(isReprint ? 'Invoice reprint sent.' : 'Invoice sent to printer.');
      return true;
    } catch {
      const failMsg = isReprint
        ? 'Reprint failed. Please check printer connection and try again.'
        : 'Printing failed. Please check printer connection and try again.';
      this.toastService.warning(failMsg);
      return false;
    }
  }

  async reprintInvoice(saleId?: string): Promise<boolean> {
    return this.printInvoice(saleId, true);
  }

  private async executePrintFlow(saleId: string, invoiceNumber: string): Promise<boolean> {
    const targetId = saleId || this.lastSaleId();
    if (!targetId) {
      this.toastService.success(`Sale completed (${invoiceNumber})`);
      return true;
    }

    try {
      const pdfResponse = await firstValueFrom(this.salesClient.getInvoicePdf(targetId));
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
      let windowOpened = false;
      try {
        const printWindow = window.open(url, '_blank');
        if (printWindow) {
          windowOpened = true;
          setTimeout(() => {
            try {
              printWindow.print();
            } catch {
              // Browser blocked automated print dialog; window opened as fallback
            }
          }, 500);
        }
      } catch {
        windowOpened = false;
      }

      // If window.open was blocked by browser popup blocker, use a hidden iframe to print
      if (!windowOpened && typeof document !== 'undefined') {
        const iframe = document.createElement('iframe');
        iframe.style.position = 'fixed';
        iframe.style.right = '0';
        iframe.style.bottom = '0';
        iframe.style.width = '0';
        iframe.style.height = '0';
        iframe.style.border = '0';
        iframe.src = url;
        document.body.appendChild(iframe);
        iframe.onload = () => {
          try {
            iframe.contentWindow?.focus();
            iframe.contentWindow?.print();
          } catch {
            // Fallback
          }
        };
      }
    } catch {
      // Fallback if window creation fails in headless / test environment
    }
  }

  private async parseSaleResponse(response: unknown): Promise<SaleCreatedResponse> {
    try {
      if (!response) {
        return {};
      }
      if (typeof response === 'object') {
        const anyResp = response as Record<string, unknown>;
        if (anyResp['saleId'] || anyResp['invoiceNumber']) {
          return {
            saleId: anyResp['saleId'] ? String(anyResp['saleId']) : undefined,
            invoiceNumber: anyResp['invoiceNumber'] ? String(anyResp['invoiceNumber']) : undefined,
            grandTotal: typeof anyResp['grandTotal'] === 'number' ? anyResp['grandTotal'] : undefined,
            amountPaid: typeof anyResp['amountPaid'] === 'number' ? anyResp['amountPaid'] : undefined,
            balance: typeof anyResp['balance'] === 'number' ? anyResp['balance'] : undefined
          };
        }
        const resp = response as { data?: { text?: () => Promise<string> } };
        if (resp && resp.data && typeof resp.data.text === 'function') {
          const text = await resp.data.text();
          if (text && text.trim().length > 0) {
            return JSON.parse(text) as SaleCreatedResponse;
          }
        }
      }
    } catch {
      // Ignored: fallback to empty response
    }
    return {};
  }

  private extractErrorMessage(err: unknown): string {
    if (err && typeof err === 'object') {
      const withMsg = err as { message?: string; response?: string; status?: number; error?: unknown };
      if (withMsg.response) {
        try {
          const parsed = JSON.parse(withMsg.response) as { message?: string; detail?: string; title?: string; errors?: Record<string, string[]> };
          if (parsed.errors) {
            const firstErr = Object.values(parsed.errors).flat()[0];
            if (firstErr) return firstErr;
          }
          if (parsed.detail) return parsed.detail;
          if (parsed.message) return parsed.message;
          if (parsed.title) return parsed.title;
        } catch {
          return withMsg.response;
        }
      }
      if (withMsg.message) {
        return withMsg.message;
      }
    }
    return 'An unexpected error occurred during checkout.';
  }
}
