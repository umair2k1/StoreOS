import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SalesClient } from '../../../core/api/generated-client';
import type { SaleDetailDto } from '../../../core/api/generated-client';
import { PermissionDirective } from '../../../core/directives/permission.directive';
import {
  PzDialogService,
  LoadingStateComponent,
  ErrorStateComponent,
  PzMoneyAliasPipe,
  PzDateAliasPipe,
  ToastService
} from '../../../shared';
import { VoidDialogComponent, VoidDialogResult } from './void-dialog.component';
import { RefundDialogComponent, RefundDialogResult } from './refund-dialog.component';
import { RecordPaymentDialogComponent, RecordPaymentDialogResult } from './record-payment-dialog.component';

@Component({
  selector: 'app-sale-detail',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    PermissionDirective,
    LoadingStateComponent,
    ErrorStateComponent,
    PzMoneyAliasPipe,
    PzDateAliasPipe
  ],
  templateUrl: './sale-detail.component.html',
  styleUrl: './sale-detail.component.scss'
})
export class SaleDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly salesClient = inject(SalesClient);
  private readonly dialogService = inject(PzDialogService);
  private readonly toastService = inject(ToastService);

  readonly saleId = signal<string>('');
  readonly sale = signal<SaleDetailDto | null>(null);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly isActive = computed(() => {
    return this.sale()?.status?.toLowerCase() === 'active';
  });

  readonly canRecordPayment = computed(() => {
    const s = this.sale();
    return (s?.balance ?? 0) > 0 && this.isActive();
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.saleId.set(id);
      this.loadSale();
    } else {
      this.errorMessage.set('Missing sale identifier in route.');
    }
  }

  loadSale(): void {
    const id = this.saleId();
    if (!id) return;

    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.salesClient.getSaleById(id).subscribe({
      next: (data) => {
        this.sale.set(data);
        this.isLoading.set(false);
      },
      error: () => {
        this.isLoading.set(false);
        this.errorMessage.set('Failed to load sale details.');
      }
    });
  }

  openRecordPaymentDialog(): void {
    const current = this.sale();
    if (!current?.id || !this.canRecordPayment()) return;

    const dialogRef = this.dialogService.open<RecordPaymentDialogComponent, any, RecordPaymentDialogResult>(
      RecordPaymentDialogComponent,
      {
        data: {
          saleId: current.id,
          invoiceNumber: current.invoiceNumber || 'Sale',
          balance: current.balance || 0
        }
      }
    );

    dialogRef.afterClosed().subscribe(async (result) => {
      if (result?.confirmed && result.amount > 0) {
        try {
          await firstValueFrom(
            this.salesClient.recordPayment(current.id!, {
              amount: result.amount,
              paymentMode: result.paymentMode
            })
          );
          this.toastService.success(`Payment of ₹${result.amount.toFixed(2)} recorded successfully.`);
          this.loadSale();
        } catch {
          this.toastService.error('Failed to record payment.');
        }
      }
    });
  }

  openVoidDialog(): void {
    const current = this.sale();
    if (!current?.id || !this.isActive()) return;

    const dialogRef = this.dialogService.open<VoidDialogComponent, any, VoidDialogResult>(
      VoidDialogComponent,
      {
        data: {
          saleId: current.id,
          invoiceNumber: current.invoiceNumber || 'Sale'
        }
      }
    );

    dialogRef.afterClosed().subscribe(async (result) => {
      if (result?.confirmed && result.reason) {
        try {
          await firstValueFrom(
            this.salesClient.voidSale(current.id!, { reason: result.reason })
          );
          this.toastService.success(`Sale ${current.invoiceNumber} voided successfully.`);
          this.loadSale();
        } catch {
          this.toastService.error('Failed to void sale.');
        }
      }
    });
  }

  openRefundDialog(): void {
    const current = this.sale();
    if (!current?.id || !this.isActive()) return;

    const dialogRef = this.dialogService.open<RefundDialogComponent, any, RefundDialogResult>(
      RefundDialogComponent,
      {
        data: {
          sale: current
        }
      }
    );

    dialogRef.afterClosed().subscribe(async (result) => {
      if (result?.confirmed && result.reason && result.refundAmount > 0) {
        try {
          await firstValueFrom(
            this.salesClient.refundSale(current.id!, {
              reason: result.reason,
              saleItemIds: result.saleItemIds.length > 0 ? result.saleItemIds : undefined,
              refundAmount: result.refundAmount
            })
          );
          this.toastService.success(`Refund of ₹${result.refundAmount.toFixed(2)} processed successfully.`);
          this.loadSale();
        } catch {
          this.toastService.error('Failed to process refund.');
        }
      }
    });
  }

  async printInvoice(): Promise<void> {
    const id = this.saleId();
    if (!id) return;

    try {
      const resp = await firstValueFrom(this.salesClient.getInvoicePdf(id));
      if (resp?.data) {
        const blobUrl = URL.createObjectURL(resp.data);
        const win = window.open(blobUrl, '_blank');
        if (win) {
          win.onload = () => win.print();
        }
      }
      this.toastService.success('Invoice print initiated.');
    } catch {
      this.toastService.warning('Failed to generate invoice PDF.');
    }
  }
}
