import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { ExportsComponent } from './exports.component';
import { ExportsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('ExportsComponent', () => {
  let fixture: ComponentFixture<ExportsComponent>;
  let component: ExportsComponent;
  let exportsClient: ExportsClient;

  function createFileResponse(payload: unknown, fileName = 'export.csv'): FileResponse {
    const text = typeof payload === 'string' ? payload : JSON.stringify(payload);
    return {
      data: new Blob([text], { type: 'text/csv' }),
      status: 200,
      fileName,
      headers: {}
    } as unknown as FileResponse;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExportsComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ExportsClient,
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

    exportsClient = TestBed.inject(ExportsClient);

    vi.spyOn(exportsClient, 'exportInventory').mockReturnValue(
      of(createFileResponse('SKU,Name,Stock\n1,Phone,5', 'inventory.csv'))
    );
    vi.spyOn(exportsClient, 'exportGstr1').mockReturnValue(
      of(createFileResponse('Invoice,GSTIN,Taxable\nINV-1,29AAAAA,1000', 'gstr1.csv'))
    );

    fixture = TestBed.createComponent(ExportsComponent);
    component = fixture.componentInstance;
  });

  it('triggers inventory export endpoint with selected categories and csv format', async () => {
    fixture.detectChanges();

    // Default categories are ['MobilePhone', 'Accessory']
    expect(component.selectedCategories()).toEqual(['MobilePhone', 'Accessory']);

    // Toggle 'Tablet' into selection
    component.toggleCategory('Tablet');
    expect(component.selectedCategories()).toContain('Tablet');

    // Trigger download
    await component.downloadInventoryExport();

    expect(exportsClient.exportInventory).toHaveBeenCalledWith(
      ['MobilePhone', 'Accessory', 'Tablet'],
      null,
      'csv'
    );
  });

  it('triggers GSTR-1 export endpoint with selected date range and csv format', async () => {
    fixture.detectChanges();

    component.gstr1From.set('2026-10-01');
    component.gstr1To.set('2026-10-08');

    await component.downloadGstr1Export();

    expect(exportsClient.exportGstr1).toHaveBeenCalledWith(
      new Date('2026-10-01'),
      new Date('2026-10-08'),
      'csv'
    );
  });

  it('triggers inventory export endpoint with selected categories and pdf format', async () => {
    fixture.detectChanges();

    await component.downloadInventoryPdf();

    expect(exportsClient.exportInventory).toHaveBeenCalledWith(
      ['MobilePhone', 'Accessory'],
      null,
      'pdf'
    );
  });
});
