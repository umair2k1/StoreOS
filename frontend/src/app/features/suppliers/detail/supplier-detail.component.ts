import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { SuppliersClient } from '../../../core/api/generated-client';
import type {
  RecordSupplierTransactionBody,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import { PzMoneyAliasPipe, PzDateAliasPipe } from '../../../shared';

export interface SupplierDetailInfo {
  id: string;
  name: string;
  phone?: string;
  address?: string;
  brandOrCategory?: string;
  notes?: string;
}

export interface SupplierTransactionItem {
  id?: string;
  type: string;
  amount: number;
  paymentMode?: string;
  referenceNo?: string;
  txnDate: Date | string;
  notes?: string;
}

export interface SupplierPayableData {
  supplierId?: string;
  balance?: number;
}

@Component({
  selector: 'app-supplier-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, PzMoneyAliasPipe, PzDateAliasPipe],
  templateUrl: './supplier-detail.component.html',
  styleUrl: './supplier-detail.component.scss'
})
export class SupplierDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly suppliersClient = inject(SuppliersClient);
  private readonly toastService = inject(ToastService);

  readonly supplierId = signal<string>('');
  readonly supplier = signal<SupplierDetailInfo | null>(null);
  readonly payableBalance = signal<number>(0);
  readonly transactions = signal<SupplierTransactionItem[]>([]);

  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Record Transaction modal state
  readonly isRecordModalOpen = signal<boolean>(false);
  readonly isSubmittingTxn = signal<boolean>(false);
  readonly txnType = signal<string>('Payment');
  readonly txnAmount = signal<number>(0);
  readonly txnPaymentMode = signal<string>('BankTransfer');
  readonly txnReferenceNo = signal<string>('');
  readonly txnDate = signal<string>(new Date().toISOString().substring(0, 10));
  readonly txnNotes = signal<string>('');

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.supplierId.set(id);
      this.loadAllData(id);
    } else {
      this.errorMessage.set('Missing supplier identifier in route.');
    }
  }

  async loadAllData(id: string): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    try {
      await Promise.all([
        this.loadProfile(id),
        this.loadPayableBalance(id),
        this.loadTransactions(id)
      ]);
    } catch {
      this.errorMessage.set('Failed to load supplier details.');
    } finally {
      this.isLoading.set(false);
    }
  }

  async loadProfile(id: string): Promise<void> {
    const resp = await firstValueFrom(this.suppliersClient.getById(id));
    const parsed = await this.parseFileResponse<SupplierDetailInfo>(resp);
    this.supplier.set(parsed);
  }

  // HARD CONSTRAINT: The displayed payable balance must ALWAYS be LIVE-derived
  // from the backend's GetSupplierPayableQuery (P-606), never computed client-side.
  async loadPayableBalance(id: string): Promise<void> {
    const resp = await firstValueFrom(this.suppliersClient.getPayable(id));
    const parsed = await this.parseFileResponse<SupplierPayableData>(resp);
    this.payableBalance.set(parsed?.balance ?? 0);
  }

  async loadTransactions(id: string): Promise<void> {
    const resp = await firstValueFrom(
      this.suppliersClient.getTransactions(id, undefined, undefined, undefined, 1, 50)
    );
    const parsed = await this.parseFileResponse<unknown>(resp);
    let list: SupplierTransactionItem[] = [];

    if (Array.isArray(parsed)) {
      list = parsed as SupplierTransactionItem[];
    } else if (parsed && typeof parsed === 'object' && 'items' in parsed) {
      const nested = parsed.items;
      if (Array.isArray(nested)) {
        list = nested as SupplierTransactionItem[];
      }
    }
    this.transactions.set(list);
  }

  openRecordModal(): void {
    this.txnType.set('Payment');
    this.txnAmount.set(0);
    this.txnPaymentMode.set('BankTransfer');
    this.txnReferenceNo.set('');
    this.txnDate.set(new Date().toISOString().substring(0, 10));
    this.txnNotes.set('');
    this.isRecordModalOpen.set(true);
  }

  closeRecordModal(): void {
    this.isRecordModalOpen.set(false);
  }

  async submitRecordTransaction(): Promise<void> {
    const id = this.supplierId();
    const amount = this.txnAmount();
    if (!id || amount <= 0 || this.isSubmittingTxn()) return;

    this.isSubmittingTxn.set(true);

    const body: RecordSupplierTransactionBody = {
      type: this.txnType(),
      amount: amount,
      paymentMode: this.txnPaymentMode() || undefined,
      referenceNo: this.txnReferenceNo().trim() || undefined,
      txnDate: new Date(this.txnDate()),
      notes: this.txnNotes().trim() || undefined
    };

    try {
      await firstValueFrom(this.suppliersClient.recordTransaction(id, body));
      this.toastService.success(`Transaction of ₹${amount} recorded.`);
      this.closeRecordModal();

      // LIVE-fetch updated payable balance and transactions from server
      await Promise.all([
        this.loadPayableBalance(id),
        this.loadTransactions(id)
      ]);
    } catch {
      this.toastService.error('Failed to record transaction.');
    } finally {
      this.isSubmittingTxn.set(false);
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp) return null;
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      if (!text) return null;
      const parsedUnknown = JSON.parse(text) as unknown;
      return parsedUnknown as T;
    }
    if (typeof resp.data === 'string') {
      const parsedUnknown = JSON.parse(resp.data) as unknown;
      return parsedUnknown as T;
    }
    const rawData = resp.data as unknown;
    return (rawData as T) || null;
  }
}
