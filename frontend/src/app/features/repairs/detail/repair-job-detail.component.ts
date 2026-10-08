import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  RepairJobsClient
} from '../../../core/api/generated-client';
import type {
  UpdateRepairJobDiagnosisBody,
  UpdateRepairJobCommercialsBody,
  ReserveRepairPartBody,
  DeliverRepairJobBody,
  BillRepairBody,
  CheckoutPayment,
  FileResponse
} from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import { PzMoneyAliasPipe, PzDateAliasPipe } from '../../../shared';
import { PaymentPanelComponent } from '../../pos/payment/payment-panel.component';

export interface RepairJobDetail {
  id: string;
  tenantId: string;
  branchId: string;
  jobNumber: string;
  customerId?: string | null;
  customerName?: string | null;
  customerPhone?: string | null;
  customerAddress?: string | null;
  deviceBrand?: string | null;
  deviceModel?: string | null;
  deviceType?: string | null;
  serialNumber?: string | null;
  imei1?: string | null;
  imei2?: string | null;
  deviceCondition?: string | null;
  accessoriesReceived?: string | null;
  problemDescription?: string | null;
  customerNotes?: string | null;
  diagnosis?: string | null;
  technicianNotes?: string | null;
  internalNotes?: string | null;
  technician?: string | null;
  estimatedCost?: number | null;
  approvedCost?: number | null;
  partsCost?: number | null;
  laborCharge?: number | null;
  discount?: number | null;
  tax?: number | null;
  finalTotal?: number | null;
  advancePaid?: number | null;
  balanceDue?: number | null;
  status: string;
  receivedAt?: Date | string | null;
  expectedCompletion?: Date | string | null;
  completedAt?: Date | string | null;
  deliveredAt?: Date | string | null;
  warrantyUntil?: string | null;
  approvalState?: string | null;
  approvalTimestamp?: Date | string | null;
  cancellationReason?: string | null;
  invoiceNumber?: string | null;
  saleId?: string | null;
}

export interface RepairJobPart {
  id: string;
  repairJobId: string;
  productId?: string | null;
  partName: string;
  quantity: number;
  unitCost: number;
  chargeAmount: number;
  status: string;
  createdAt: Date | string;
}

interface BillRepairResponse {
  saleId: string;
  invoiceNumber: string;
  grandTotal: number;
  amountPaid: number;
  balance: number;
}

@Component({
  selector: 'app-repair-job-detail',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    PzMoneyAliasPipe,
    PaymentPanelComponent
  ],
  templateUrl: './repair-job-detail.component.html',
  styleUrl: './repair-job-detail.component.scss'
})
export class RepairJobDetailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly repairJobsClient = inject(RepairJobsClient);
  private readonly toastService = inject(ToastService);

  readonly jobId = signal<string>('');
  readonly job = signal<RepairJobDetail | null>(null);
  readonly parts = signal<RepairJobPart[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Diagnosis Edit Form State
  readonly diagDiagnosis = signal<string>('');
  readonly diagTechnicianNotes = signal<string>('');
  readonly diagInternalNotes = signal<string>('');
  readonly diagTechnician = signal<string>('');
  readonly isSavingDiagnosis = signal<boolean>(false);

  // Commercials Edit Form State
  readonly commEstimatedCost = signal<number | null>(null);
  readonly commApprovedCost = signal<number | null>(null);
  readonly commPartsCost = signal<number | null>(null);
  readonly commLaborCharge = signal<number | null>(null);
  readonly commDiscount = signal<number | null>(null);
  readonly commTax = signal<number | null>(null);
  readonly commFinalTotal = signal<number | null>(null);
  readonly commAdvancePaid = signal<number | null>(null);
  readonly isSavingCommercials = signal<boolean>(false);

  // Dynamic balance due auto-computed for display mirroring server's calculation
  readonly computedBalanceDue = computed(() => {
    const total = this.commFinalTotal() ?? this.commEstimatedCost() ?? 0;
    const advance = this.commAdvancePaid() ?? 0;
    return total - advance;
  });

  // Reserve Part Modal State
  readonly isReservePartModalOpen = signal<boolean>(false);
  readonly isReservingPart = signal<boolean>(false);
  readonly partName = signal<string>('');
  readonly partQuantity = signal<number>(1);
  readonly partUnitCost = signal<number>(0);
  readonly partChargeAmount = signal<number>(0);

  // Billing Modal State (reusing PaymentPanelComponent)
  readonly isBillingModalOpen = signal<boolean>(false);
  readonly isSubmittingBilling = signal<boolean>(false);
  readonly billingPayments = signal<CheckoutPayment[]>([]);

  readonly approvalStates: readonly string[] = ['Pending', 'Approved', 'Rejected'];

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.jobId.set(id);
      this.loadJobData(id);
    }
  }

  async loadJobData(id: string): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      await Promise.all([this.fetchJob(id), this.fetchParts(id)]);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load repair job.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  async fetchJob(id: string): Promise<void> {
    const resp = await firstValueFrom(this.repairJobsClient.getById(id));
    const parsed = await this.parseFileResponse<RepairJobDetail>(resp);
    if (parsed) {
      this.job.set(parsed);
      this.syncFormSignals(parsed);
    }
  }

  async fetchParts(id: string): Promise<void> {
    const resp = await firstValueFrom(this.repairJobsClient.getParts(id));
    const parsed = await this.parseFileResponse<RepairJobPart[]>(resp);
    this.parts.set(Array.isArray(parsed) ? parsed : []);
  }

  private syncFormSignals(data: RepairJobDetail): void {
    this.diagDiagnosis.set(data.diagnosis || '');
    this.diagTechnicianNotes.set(data.technicianNotes || '');
    this.diagInternalNotes.set(data.internalNotes || '');
    this.diagTechnician.set(data.technician || '');

    this.commEstimatedCost.set(data.estimatedCost ?? null);
    this.commApprovedCost.set(data.approvedCost ?? null);
    this.commPartsCost.set(data.partsCost ?? null);
    this.commLaborCharge.set(data.laborCharge ?? null);
    this.commDiscount.set(data.discount ?? null);
    this.commTax.set(data.tax ?? null);
    this.commFinalTotal.set(data.finalTotal ?? null);
    this.commAdvancePaid.set(data.advancePaid ?? null);
  }

  async saveDiagnosis(): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    this.isSavingDiagnosis.set(true);
    try {
      const body: UpdateRepairJobDiagnosisBody = {
        diagnosis: this.diagDiagnosis().trim() || undefined,
        technicianNotes: this.diagTechnicianNotes().trim() || undefined,
        internalNotes: this.diagInternalNotes().trim() || undefined,
        technician: this.diagTechnician().trim() || undefined
      };
      await firstValueFrom(this.repairJobsClient.updateDiagnosis(id, body));
      this.toastService.success('Diagnosis updated successfully.');
      await this.fetchJob(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update diagnosis.';
      this.toastService.error(msg);
    } finally {
      this.isSavingDiagnosis.set(false);
    }
  }

  async saveCommercials(): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    this.isSavingCommercials.set(true);
    try {
      const body: UpdateRepairJobCommercialsBody = {
        estimatedCost: this.commEstimatedCost() ?? undefined,
        approvedCost: this.commApprovedCost() ?? undefined,
        partsCost: this.commPartsCost() ?? undefined,
        laborCharge: this.commLaborCharge() ?? undefined,
        discount: this.commDiscount() ?? undefined,
        tax: this.commTax() ?? undefined,
        finalTotal: this.commFinalTotal() ?? undefined,
        advancePaid: this.commAdvancePaid() ?? undefined
      };
      await firstValueFrom(this.repairJobsClient.updateCommercials(id, body));
      this.toastService.success('Commercials updated successfully.');
      await this.fetchJob(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update commercials.';
      this.toastService.error(msg);
    } finally {
      this.isSavingCommercials.set(false);
    }
  }

  async setApprovalState(state: string): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    try {
      await firstValueFrom(this.repairJobsClient.recordApproval(id, { approvalState: state }));
      this.toastService.success(`Approval status updated to ${state}.`);
      await this.fetchJob(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to update approval status.';
      this.toastService.error(msg);
    }
  }

  openReservePartModal(): void {
    this.partName.set('');
    this.partQuantity.set(1);
    this.partUnitCost.set(0);
    this.partChargeAmount.set(0);
    this.isReservePartModalOpen.set(true);
  }

  closeReservePartModal(): void {
    this.isReservePartModalOpen.set(false);
  }

  async submitReservePart(): Promise<void> {
    const id = this.jobId();
    const name = this.partName().trim();
    const qty = this.partQuantity();

    if (!id || !name || qty <= 0) {
      this.toastService.error('Valid part name and quantity required.');
      return;
    }

    this.isReservingPart.set(true);
    try {
      const body: ReserveRepairPartBody = {
        partName: name,
        quantity: qty,
        unitCost: this.partUnitCost(),
        chargeAmount: this.partChargeAmount()
      };
      await firstValueFrom(this.repairJobsClient.reservePart(id, body));
      this.toastService.success(`Part '${name}' reserved.`);
      this.closeReservePartModal();
      await this.fetchParts(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to reserve part.';
      this.toastService.error(msg);
    } finally {
      this.isReservingPart.set(false);
    }
  }

  async consumePart(part: RepairJobPart): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    try {
      await firstValueFrom(this.repairJobsClient.consumePart(id, part.id));
      this.toastService.success(`Part '${part.partName}' consumed.`);
      await this.fetchParts(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to consume part.';
      this.toastService.error(msg);
    }
  }

  async returnPart(part: RepairJobPart): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    try {
      await firstValueFrom(this.repairJobsClient.returnPart(id, part.id));
      this.toastService.success(`Part '${part.partName}' returned.`);
      await this.fetchParts(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to return part.';
      this.toastService.error(msg);
    }
  }

  // HARD CONSTRAINT: Deliver button is visible only when Status == 'ReadyForPickup'
  async deliverJob(): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    try {
      const body: DeliverRepairJobBody = {};
      await firstValueFrom(this.repairJobsClient.deliver(id, body));
      this.toastService.success('Repair job delivered to customer.');
      await this.fetchJob(id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to deliver repair job.';
      this.toastService.error(msg);
    }
  }

  // HARD CONSTRAINT: Bill button is visible once FinalTotal is set and no sale is yet linked
  openBillingModal(): void {
    const currentJob = this.job();
    if (!currentJob) return;

    const total = currentJob.finalTotal ?? 0;
    this.billingPayments.set([
      { paymentMode: 'cash', amount: total }
    ]);
    this.isBillingModalOpen.set(true);
  }

  closeBillingModal(): void {
    this.isBillingModalOpen.set(false);
  }

  onPaymentsChange(payments: CheckoutPayment[]): void {
    this.billingPayments.set(payments);
  }

  async submitBilling(): Promise<void> {
    const id = this.jobId();
    if (!id) return;

    this.isSubmittingBilling.set(true);
    try {
      const body: BillRepairBody = {
        payments: this.billingPayments()
      };
      const resp = await firstValueFrom(this.repairJobsClient.billRepair(id, body));
      const result = await this.parseFileResponse<BillRepairResponse>(resp);

      this.toastService.success('Repair job billed successfully.');
      this.closeBillingModal();

      if (result?.saleId) {
        await this.router.navigate(['/sales', result.saleId]);
      } else {
        await this.fetchJob(id);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to bill repair job.';
      this.toastService.error(msg);
    } finally {
      this.isSubmittingBilling.set(false);
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp || !resp.data) return null;
    if (typeof resp.data === 'string') {
      return JSON.parse(resp.data) as T;
    }
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      return JSON.parse(text) as T;
    }
    return resp.data as T;
  }
}
