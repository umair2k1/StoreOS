import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SuppliersListComponent } from './suppliers-list.component';
import { SuppliersClient } from '../../core/api/generated-client';
import type { CreateSupplierCommand, FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('SuppliersListComponent', () => {
  let fixture: ComponentFixture<SuppliersListComponent>;
  let component: SuppliersListComponent;
  let suppliersClient: SuppliersClient;

  function createFileResponse(payload: unknown): FileResponse {
    return {
      data: new Blob([JSON.stringify(payload)], { type: 'application/json' }),
      status: 200,
      fileName: 'suppliers.json',
      headers: {}
    };
  }

  const sampleSuppliers = [
    {
      id: 'sup-1',
      name: 'Global Tech Distribution',
      phone: '9876543210',
      address: 'Industrial Area',
      brandOrCategory: 'Smartphones'
    },
    {
      id: 'sup-2',
      name: 'Prime Accessories Ltd',
      phone: '9123456780',
      address: 'City Center',
      brandOrCategory: 'Accessories'
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SuppliersListComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        SuppliersClient,
        ToastService
      ]
    }).compileComponents();

    suppliersClient = TestBed.inject(SuppliersClient);
    vi.spyOn(suppliersClient, 'list').mockReturnValue(of(createFileResponse(sampleSuppliers)));
    vi.spyOn(suppliersClient, 'create').mockReturnValue(of(createFileResponse({ id: 'sup-new' })));

    fixture = TestBed.createComponent(SuppliersListComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders suppliers list and triggers search query', async () => {
    const listSpy = vi.spyOn(suppliersClient, 'list').mockReturnValue(of(createFileResponse([sampleSuppliers[0]])));

    await component.onSearch('Global');
    fixture.detectChanges();

    expect(listSpy).toHaveBeenCalledWith('Global', 1, 20);
    expect(component.suppliers().length).toBe(1);
    expect(component.suppliers()[0].name).toBe('Global Tech Distribution');
  });

  it('submits create supplier command and refreshes list', async () => {
    const createSpy = vi.spyOn(suppliersClient, 'create').mockReturnValue(of(createFileResponse({ id: 'sup-3' })));
    const listSpy = vi.spyOn(suppliersClient, 'list').mockReturnValue(of(createFileResponse(sampleSuppliers)));

    component.openCreateModal();
    fixture.detectChanges();
    expect(component.isCreateModalOpen()).toBe(true);

    component.newName.set('Apex Electronics');
    component.newPhone.set('8899776655');
    component.newAddress.set('Warehouse 4B');
    component.newCategory.set('Audio & Earphones');
    fixture.detectChanges();

    await component.submitCreateSupplier();
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalledTimes(1);
    const sentCommand = createSpy.mock.calls[0][0] as CreateSupplierCommand;
    expect(sentCommand.name).toBe('Apex Electronics');
    expect(sentCommand.phone).toBe('8899776655');
    expect(sentCommand.address).toBe('Warehouse 4B');
    expect(sentCommand.brandOrCategory).toBe('Audio & Earphones');

    expect(component.isCreateModalOpen()).toBe(false);
    expect(listSpy).toHaveBeenCalled();
  });
});
