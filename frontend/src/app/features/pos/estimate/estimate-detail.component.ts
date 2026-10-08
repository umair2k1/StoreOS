import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { PosClient, SalesClient } from '../../../core/api/generated-client';
import type {
  SaleDetailDto,
  CheckoutPayment,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import {
  LoadingStateComponent,
  ErrorStateComponent,
  PzMoneyAliasPipe,
  PzDateAliasPipe
} from '../../../shared';
import { PaymentPanelComponent } from '../payment/payment-panel.component';

@Component({
  selector: 'app-estimate-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    LoadingStateComponent,
    ErrorStateComponent,
    PzMoneyAliasPipe,
    PzDateAliasPipe,
    PaymentPanelComponent
  ],
  templateUrl: './estimate-detail.component.html',
  styleUrl: './estimate-detail.component.scss'
})
export class EstimateDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly posClient = inject(PosClient);
  private readonly salesClient = inject(SalesClient);
  private readonly toastService = inject(ToastService);

  readonly estimateId = signal<string>('');
  readonly estimate = signal<SaleDetailDto | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly showPaymentPrompt = signal<boolean>(false);
  readonly payments = signal<CheckoutPayment[]>([]);
  readonly isConverting = signal<boolean>(false);

  readonly isConverted = computed(() => {
    const est = this.estimate();
    return Boolean(est?.linkedTaxInvoiceId || est?.status?.toLowerCase() === 'converted');
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.estimateId.set(id);
      this.loadEstimate();
    } else {
      this.errorMessage.set('Missing estimate identifier in route.');
    }
  }

  loadEstimate(): void {
    const id = this.estimateId();
    if (!id) {
      return;
    }

    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.salesClient.getSaleById(id).subscribe({
      next: (data) => {
        this.estimate.set(data);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.isLoading.set(false);
        this.errorMessage.set(this.extractErrorMessage(err) || 'Failed to load estimate details.');
      }
    });
  }

  startConversion(): void {
    this.showPaymentPrompt.set(true);
  }

  cancelConversion(): void {
    this.showPaymentPrompt.set(false);
  }

  onPaymentsChange(payments: CheckoutPayment[]): void {
    this.payments.set(payments);
  }

  async convert(): Promise<void> {
    const id = this.estimateId();
    if (!id) {
      this.toastService.error('Invalid estimate identifier.');
      return;
    }

    this.isConverting.set(true);

    try {
      const response = await firstValueFrom(
        this.posClient.convertEstimate(id, { payments: this.payments() })
      );

      const parsed = await this.parseFileResponseJson(response);
      const newSaleId = parsed.saleId || parsed.newSaleId || parsed.id;
      const invoiceNumber = parsed.invoiceNumber || 'Tax Invoice';

      this.toastService.success(`Estimate converted to invoice (${invoiceNumber})`);

      if (newSaleId) {
        await this.router.navigate(['/sales', newSaleId]);
      } else {
        await this.router.navigate(['/sales']);
      }
    } catch (err: unknown) {
      const msg = this.extractErrorMessage(err);
      this.toastService.error(`Failed to convert estimate: ${msg}`);
    } finally {
      this.isConverting.set(false);
    }
  }

  private async parseFileResponseJson(response: FileResponse): Promise<any> {
    if (!response) {
      return {};
    }
    if (response.data instanceof Blob) {
      const text = await response.data.text();
      if (text) {
        try {
          return JSON.parse(text);
        } catch {
          return {};
        }
      }
    } else if (typeof response.data === 'string') {
      try {
        return JSON.parse(response.data);
      } catch {
        return {};
      }
    } else if (response.data && typeof response.data === 'object') {
      return response.data;
    }
    return {};
  }

  private extractErrorMessage(err: unknown): string {
    if (!err) {
      return 'An unexpected error occurred.';
    }
    if (typeof err === 'string') {
      return err;
    }
    const withMsg = err as { message?: string; response?: string };
    if (withMsg.message) {
      return withMsg.message;
    }
    if (withMsg.response) {
      try {
        const parsed = JSON.parse(withMsg.response);
        if (parsed.message) {
          return parsed.message;
        }
      } catch {
        return withMsg.response;
      }
    }
    return 'An unexpected error occurred.';
  }
}
