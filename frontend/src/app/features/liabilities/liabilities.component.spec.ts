import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { LiabilitiesComponent } from './liabilities.component';
import { LiabilitiesClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('LiabilitiesComponent', () => {
  let fixture: ComponentFixture<LiabilitiesComponent>;
  let component: LiabilitiesComponent;
  let liabilitiesClient: LiabilitiesClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockLiabilities = [
    {
      id: '11111111-1111-1111-1111-111111111111',
      title: 'Store Expansion Loan',
      type: 'Loan',
      lenderName: 'Apex Capital',
      totalPrincipal: 500000,
      currentBalance: 350000,
      dueDate: '2026-12-31',
      status: 'Active',
      notes: 'Quarterly repayment'
    },
    {
      id: '22222222-2222-2222-2222-222222222222',
      title: 'Distributor Credit Line',
      type: 'Supplier Credit',
      lenderName: 'National Logistics',
      totalPrincipal: 100000,
      currentBalance: 100000,
      dueDate: null,
      status: 'Active',
      notes: null
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [LiabilitiesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        LiabilitiesClient,
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

    liabilitiesClient = TestBed.inject(LiabilitiesClient);

    vi.spyOn(liabilitiesClient, 'list').mockReturnValue(
      of(createFileResponse({ items: mockLiabilities, totalCount: 2, page: 1, pageSize: 50 }))
    );
    vi.spyOn(liabilitiesClient, 'create').mockReturnValue(
      of(createFileResponse({ id: '33333333-3333-3333-3333-333333333333' }))
    );
    vi.spyOn(liabilitiesClient, 'recordRepayment').mockReturnValue(
      of(createFileResponse({ id: 'pay-12345' }))
    );

    fixture = TestBed.createComponent(LiabilitiesComponent);
    component = fixture.componentInstance;
  });

  it('renders liabilities list with Title, Type, LenderName, TotalPrincipal, CurrentBalance, DueDate, Status', async () => {
    fixture.detectChanges();
    await component.loadLiabilities();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.liability-row');
    expect(rows.length).toBe(2);

    const firstRow = rows[0];
    expect(firstRow.querySelector('.col-title')?.textContent).toContain('Store Expansion Loan');
    expect(firstRow.querySelector('.col-type')?.textContent).toContain('Loan');
    expect(firstRow.querySelector('.col-lender')?.textContent).toContain('Apex Capital');
    expect(firstRow.querySelector('.col-principal')?.textContent).toContain('5,00,000');
    expect(firstRow.querySelector('.col-balance')?.textContent).toContain('3,50,000');
    expect(firstRow.querySelector('.col-status')?.textContent).toContain('Active');
  });

  it('recording a repayment updates the displayed CurrentBalance and does not expose any control to edit TotalPrincipal', async () => {
    fixture.detectChanges();
    await component.loadLiabilities();
    fixture.detectChanges();

    const item = component.liabilities()[0];
    component.openRepaymentModal(item);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;

    // Verify modal is open
    expect(component.isRepaymentModalOpen()).toBe(true);

    // Verify HARD CONSTRAINT: No UI control allows editing TotalPrincipal on the repayment form
    const repaymentModal = compiled.querySelector('.modal-card');
    expect(repaymentModal).toBeTruthy();

    const totalPrincipalInputs = repaymentModal?.querySelectorAll('input[name*="principal" i], input[name*="Principal" i]');
    expect(totalPrincipalInputs?.length).toBe(0);

    const immutablePrincipalDisplay = repaymentModal?.querySelector('.immutable-principal-display');
    expect(immutablePrincipalDisplay).toBeTruthy();
    expect(immutablePrincipalDisplay?.textContent).toContain('5,00,000');

    // Fill repayment details
    component.repaymentAmount.set(50000);
    component.repaymentMode.set('Bank Transfer');
    component.repaymentDate.set('2026-10-08');
    component.repaymentNotes.set('Part payment via NEFT');

    // Mock subsequent list call to reflect updated balance from server
    vi.spyOn(liabilitiesClient, 'recordRepayment').mockImplementation(() => {
      vi.spyOn(liabilitiesClient, 'list').mockReturnValue(
        of(createFileResponse({
          items: [
            { ...mockLiabilities[0], currentBalance: 300000 },
            mockLiabilities[1]
          ],
          totalCount: 2,
          page: 1,
          pageSize: 50
        }))
      );
      return of(createFileResponse({ id: 'pay-12345' }));
    });

    // Submit repayment
    await component.submitRepayment();
    fixture.detectChanges();

    // Verify recordRepayment API call
    expect(liabilitiesClient.recordRepayment).toHaveBeenCalledWith(
      item.id,
      expect.objectContaining({
        amount: 50000,
        paymentMode: 'Bank Transfer'
      })
    );

    // Verify CurrentBalance updated (350,000 - 50,000 = 300,000)
    const updatedItem = component.liabilities().find(x => x.id === item.id);
    expect(updatedItem?.currentBalance).toBe(300000);

    const updatedRow = compiled.querySelector(`tr[data-id="${item.id}"]`);
    expect(updatedRow?.querySelector('.col-balance')?.textContent).toContain('3,00,000');
  });
});
