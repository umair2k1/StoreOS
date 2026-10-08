import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { OverlayModule } from '@angular/cdk/overlay';
import { of } from 'rxjs';
import { RepairJobDetailComponent } from './repair-job-detail.component';
import type { RepairJobDetail, RepairJobPart } from './repair-job-detail.component';
import { RepairJobsClient } from '../../../core/api/generated-client';
import type { FileResponse } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';
import { PzDialogService } from '../../../shared/components/pz-dialog/pz-dialog.service';

describe('RepairJobDetailComponent', () => {
  let fixture: ComponentFixture<RepairJobDetailComponent>;
  let component: RepairJobDetailComponent;
  let repairJobsClient: RepairJobsClient;
  let router: Router;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const baseMockJob: RepairJobDetail = {
    id: 'job-1111-1111-1111-1111',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    jobNumber: 'REP-2026-0001',
    customerName: 'Aarav Patel',
    customerPhone: '9876543210',
    customerAddress: '123 MG Road',
    deviceBrand: 'Apple',
    deviceModel: 'iPhone 13',
    deviceType: 'Phone',
    serialNumber: 'SN12345678',
    imei1: '351234567890123',
    deviceCondition: 'Scratched screen',
    accessoriesReceived: 'Case',
    problemDescription: 'Cracked screen and touch unresponsive',
    diagnosis: 'Digitizer malfunction',
    technicianNotes: 'Requires screen replacement',
    technician: 'Ramesh',
    estimatedCost: 3500,
    approvedCost: 3500,
    partsCost: 2500,
    laborCharge: 1000,
    finalTotal: 3500,
    advancePaid: 500,
    balanceDue: 3000,
    status: 'InRepair',
    saleId: null
  };

  const mockParts: RepairJobPart[] = [
    {
      id: 'part-1111-1111-1111-1111',
      repairJobId: 'job-1111-1111-1111-1111',
      partName: 'iPhone 13 OLED Assembly',
      quantity: 1,
      unitCost: 2000,
      chargeAmount: 2500,
      status: 'Reserved',
      createdAt: '2026-10-08T10:00:00Z'
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepairJobDetailComponent, OverlayModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        PzDialogService,
        RepairJobsClient,
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: {
                get: (key: string) => (key === 'id' ? 'job-1111-1111-1111-1111' : null)
              }
            }
          }
        },
        {
          provide: Router,
          useValue: {
            navigate: vi.fn()
          }
        },
        {
          provide: ToastService,
          useValue: {
            success: vi.fn(),
            error: vi.fn(),
            info: vi.fn(),
            warning: vi.fn()
          }
        }
      ]
    }).compileComponents();

    repairJobsClient = TestBed.inject(RepairJobsClient);
    router = TestBed.inject(Router);

    vi.spyOn(repairJobsClient, 'getById').mockReturnValue(
      of(createFileResponse(baseMockJob))
    );
    vi.spyOn(repairJobsClient, 'getParts').mockReturnValue(
      of(createFileResponse(mockParts))
    );
    vi.spyOn(repairJobsClient, 'reservePart').mockReturnValue(
      of(createFileResponse({ id: 'part-new' }))
    );
    vi.spyOn(repairJobsClient, 'consumePart').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(repairJobsClient, 'returnPart').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(repairJobsClient, 'deliver').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(repairJobsClient, 'billRepair').mockReturnValue(
      of(createFileResponse({
        saleId: 'sale-9999-9999-9999-9999',
        invoiceNumber: 'INV-2026-0001',
        grandTotal: 3500,
        amountPaid: 3500,
        balance: 0
      }))
    );

    fixture = TestBed.createComponent(RepairJobDetailComponent);
    component = fixture.componentInstance;
  });

  it('(a) the Deliver button only appears when Status is ReadyForPickup', async () => {
    // 1. Initial status is 'InRepair' -> Deliver button must NOT appear
    fixture.detectChanges();
    await component.loadJobData('job-1111-1111-1111-1111');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-deliver')).toBeNull();

    // 2. Change status to 'ReadyForPickup' -> Deliver button MUST appear
    component.job.set({ ...baseMockJob, status: 'ReadyForPickup' });
    fixture.detectChanges();

    const deliverBtn = compiled.querySelector('.btn-deliver') as HTMLButtonElement;
    expect(deliverBtn).toBeTruthy();

    deliverBtn.click();
    expect(repairJobsClient.deliver).toHaveBeenCalledWith('job-1111-1111-1111-1111', {});
  });

  it('(b) the Bill button only appears once FinalTotal is set and no sale is yet linked', async () => {
    // 1. FinalTotal = 3500 and saleId = null -> Bill button MUST appear
    fixture.detectChanges();
    await component.loadJobData('job-1111-1111-1111-1111');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.btn-bill')).toBeTruthy();

    // 2. FinalTotal = 0 or null -> Bill button must NOT appear
    component.job.set({ ...baseMockJob, finalTotal: 0 });
    fixture.detectChanges();
    expect(compiled.querySelector('.btn-bill')).toBeNull();

    component.job.set({ ...baseMockJob, finalTotal: null });
    fixture.detectChanges();
    expect(compiled.querySelector('.btn-bill')).toBeNull();

    // 3. Sale is already linked (saleId != null) -> Bill button must NOT appear (shows View Sale instead)
    component.job.set({ ...baseMockJob, finalTotal: 3500, saleId: 'sale-already-linked' });
    fixture.detectChanges();
    expect(compiled.querySelector('.btn-bill')).toBeNull();
    expect(compiled.querySelector('.btn-view-sale')).toBeTruthy();
  });

  it('(c) the parts Reserve/Consume/Return actions call their correct endpoints and refresh the parts list', async () => {
    fixture.detectChanges();
    await component.loadJobData('job-1111-1111-1111-1111');
    fixture.detectChanges();

    // 1. Reserve Part
    component.openReservePartModal();
    component.partName.set('Adhesive Gasket');
    component.partQuantity.set(2);
    component.partUnitCost.set(50);
    component.partChargeAmount.set(100);

    await component.submitReservePart();
    expect(repairJobsClient.reservePart).toHaveBeenCalledWith(
      'job-1111-1111-1111-1111',
      expect.objectContaining({
        partName: 'Adhesive Gasket',
        quantity: 2,
        unitCost: 50,
        chargeAmount: 100
      })
    );
    expect(repairJobsClient.getParts).toHaveBeenCalled();

    // 2. Consume Part
    const reservedPart = component.parts()[0];
    await component.consumePart(reservedPart);
    expect(repairJobsClient.consumePart).toHaveBeenCalledWith(
      'job-1111-1111-1111-1111',
      reservedPart.id
    );

    // 3. Return Part
    await component.returnPart(reservedPart);
    expect(repairJobsClient.returnPart).toHaveBeenCalledWith(
      'job-1111-1111-1111-1111',
      reservedPart.id
    );
  });

  it('(d) billing successfully navigates to the resulting sale detail view', async () => {
    fixture.detectChanges();
    await component.loadJobData('job-1111-1111-1111-1111');
    fixture.detectChanges();

    component.openBillingModal();
    fixture.detectChanges();

    expect(component.isBillingModalOpen()).toBe(true);

    // Submit billing
    await component.submitBilling();

    expect(repairJobsClient.billRepair).toHaveBeenCalledWith(
      'job-1111-1111-1111-1111',
      expect.objectContaining({
        payments: expect.any(Array)
      })
    );

    expect(router.navigate).toHaveBeenCalledWith(['/sales', 'sale-9999-9999-9999-9999']);
  });
});
