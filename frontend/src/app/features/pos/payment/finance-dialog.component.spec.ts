import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { FinanceDialogComponent } from './finance-dialog.component';
import type { FinanceDialogData } from './finance-dialog.component';
import { SettingsClient } from '../../../core/api/generated-client';
import type { FinanceProviderDto, CheckoutFinanceDetails, FileResponse } from '../../../core/api/generated-client';
import { PzDialogRef, PZ_DIALOG_DATA } from '../../../shared/components/pz-dialog/pz-dialog.service';
import { ToastService } from '../../../shared/services/toast.service';

describe('FinanceDialogComponent', () => {
  let fixture: ComponentFixture<FinanceDialogComponent>;
  let component: FinanceDialogComponent;
  let settingsClient: SettingsClient;

  const mockDialogData: FinanceDialogData = {
    grandTotal: 15000,
    initialDownPayment: 3000,
    initialTenureMonths: 12,
    initialInterestRate: 0
  };

  const mockDialogRef = {
    close: vi.fn(),
    afterClosed: () => of(undefined),
    isOpen: () => true
  };

  const mockProviders: FinanceProviderDto[] = [
    { normalizedName: 'bajaj', displayName: 'Bajaj Finance' },
    { normalizedName: 'hdb', displayName: 'HDB Financial Services' }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FinanceDialogComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        SettingsClient,
        ToastService,
        { provide: PZ_DIALOG_DATA, useValue: mockDialogData },
        { provide: PzDialogRef, useValue: mockDialogRef }
      ]
    }).compileComponents();

    settingsClient = TestBed.inject(SettingsClient);
    vi.spyOn(settingsClient, 'getFinanceProviders').mockReturnValue(of(mockProviders));

    fixture = TestBed.createComponent(FinanceDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('loads finance providers on init and populates dropdown', () => {
    expect(component.providers().length).toBe(2);
    expect(component.selectedProvider()).toBe('bajaj');

    const select = fixture.nativeElement.querySelector('select#providerSelect') as HTMLSelectElement;
    expect(select).toBeTruthy();
    expect(select.options.length).toBe(2);
  });

  it('computes live EMI preview reactively based on parameters', () => {
    // Grand total: 15000, Down payment: 3000 -> Principal: 12000
    // Rate: 0, Tenure: 12 -> 1000/mo
    expect(component.emiPreview().principal).toBe(12000);
    expect(component.emiPreview().emi).toBe(1000);
    expect(component.emiPreview().totalPayable).toBe(15000);
    expect(component.emiPreview().error).toBeNull();

    // Change rate to 12%
    component.annualInterestRate.set(12);
    fixture.detectChanges();

    // 12000 at 12% for 12 months = 1066.19
    expect(component.emiPreview().emi).toBe(1066.19);
    expect(component.emiPreview().totalPayable).toBe(15794.28);
  });

  it('supports inline add-new-provider flow with API persistence', () => {
    const dummyResponse: FileResponse = { data: new Blob(), status: 200, headers: {} };
    const addSpy = vi.spyOn(settingsClient, 'addFinanceProvider').mockReturnValue(of(dummyResponse));

    // Toggle add provider view
    component.toggleAddProvider();
    fixture.detectChanges();

    expect(component.isAddingProvider()).toBe(true);
    const nameInput = fixture.nativeElement.querySelector('.add-provider-inline input') as HTMLInputElement;
    expect(nameInput).toBeTruthy();

    // Enter new provider
    component.newProviderName.set('TVS Credit');
    fixture.detectChanges();

    const updatedProviders: FinanceProviderDto[] = [
      ...mockProviders,
      { normalizedName: 'tvs_credit', displayName: 'TVS Credit' }
    ];
    vi.spyOn(settingsClient, 'getFinanceProviders').mockReturnValue(of(updatedProviders));

    component.saveNewProvider();
    fixture.detectChanges();

    expect(addSpy).toHaveBeenCalledWith({ displayName: 'TVS Credit' });
    expect(component.isAddingProvider()).toBe(false);
    expect(component.providers().length).toBe(3);
    expect(component.selectedProvider()).toBe('tvs_credit');
  });

  it('confirms financing and passes CheckoutFinanceDetails to dialogRef.close', () => {
    component.selectedProvider.set('bajaj');
    component.customFinancerName.set('Agent Ramesh');
    component.downPayment.set(3000);
    component.tenureMonths.set(12);
    component.annualInterestRate.set(12);
    fixture.detectChanges();

    component.onConfirm();

    expect(mockDialogRef.close).toHaveBeenCalledWith({
      financeProvider: 'bajaj',
      financerName: 'Agent Ramesh',
      downPayment: 3000,
      tenureMonths: 12,
      annualInterestRate: 12
    } as CheckoutFinanceDetails);
  });
});
