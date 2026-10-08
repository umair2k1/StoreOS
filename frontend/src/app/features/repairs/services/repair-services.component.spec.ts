import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RepairServicesComponent } from './repair-services.component';
import { RepairServicesClient, SettingsClient } from '../../../core/api/generated-client';
import type { FileResponse } from '../../../core/api/generated-client';
import { ToastService } from '../../../shared/services/toast.service';

describe('RepairServicesComponent', () => {
  let fixture: ComponentFixture<RepairServicesComponent>;
  let component: RepairServicesComponent;
  let repairServicesClient: RepairServicesClient;
  let settingsClient: SettingsClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockServices = [
    {
      id: 'aaaa1111-1111-1111-1111-111111111111',
      name: 'Screen Replacement',
      description: 'OLED / LCD glass replacement',
      standardCharge: 2500,
      taxable: true,
      gstRate: 18,
      sacCode: '998713',
      estimatedDurationMinutes: 60,
      warrantyDays: 30,
      isActive: true
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RepairServicesComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        RepairServicesClient,
        SettingsClient,
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

    repairServicesClient = TestBed.inject(RepairServicesClient);
    settingsClient = TestBed.inject(SettingsClient);

    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: true }))
    );
    vi.spyOn(repairServicesClient, 'list').mockReturnValue(
      of(createFileResponse({ items: mockServices, totalCount: 1, page: 1, pageSize: 50 }))
    );
    vi.spyOn(repairServicesClient, 'create').mockReturnValue(
      of(createFileResponse({ id: 'bbbb2222-2222-2222-2222-222222222222' }))
    );
    vi.spyOn(repairServicesClient, 'update').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(repairServicesClient, 'deactivate').mockReturnValue(
      of(createFileResponse({}))
    );

    fixture = TestBed.createComponent(RepairServicesComponent);
    component = fixture.componentInstance;
  });

  it('service CRUD displays correct SAC/GST/duration/warranty defaults on create', async () => {
    fixture.detectChanges();
    await component.initComponent();
    fixture.detectChanges();

    // Verify initial table rendering
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.col-name')?.textContent).toContain('Screen Replacement');
    expect(compiled.querySelector('.col-sac')?.textContent).toContain('998713');

    // Open create modal
    component.openCreateModal();
    fixture.detectChanges();

    // Verify P-701 documented defaults
    expect(component.formSacCode()).toBe('998713');
    expect(component.formGstRate()).toBe(18);
    expect(component.formEstimatedDuration()).toBe(60);
    expect(component.formWarrantyDays()).toBe(30);
    expect(component.formTaxable()).toBe(true);

    // Fill new service details
    component.formName.set('Battery Swap');
    component.formStandardCharge.set(1200);

    await component.saveService();
    fixture.detectChanges();

    expect(repairServicesClient.create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Battery Swap',
        standardCharge: 1200,
        sacCode: '998713',
        gstRate: 18,
        estimatedDurationMinutes: 60,
        warrantyDays: 30,
        taxable: true
      })
    );
  });

  it('shows disabled message instead of catalogue content when repairs feature is disabled', async () => {
    vi.spyOn(settingsClient, 'getRepairsEnabled').mockReturnValue(
      of(createFileResponse({ enabled: false }))
    );

    fixture.detectChanges();
    await component.initComponent();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.isRepairsEnabled()).toBe(false);
    expect(compiled.querySelector('.module-disabled-card')).toBeTruthy();
    expect(compiled.querySelector('.disabled-title')?.textContent).toContain('Repairs Module Is Not Enabled');
    expect(compiled.querySelector('.services-table')).toBeFalsy();
  });
});
