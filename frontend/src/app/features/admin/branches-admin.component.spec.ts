import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { BranchesAdminComponent } from './branches-admin.component';
import type { BranchItem } from './branches-admin.component';
import { BranchesClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('BranchesAdminComponent', () => {
  let fixture: ComponentFixture<BranchesAdminComponent>;
  let component: BranchesAdminComponent;
  let branchesClient: BranchesClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockBranches: BranchItem[] = [
    {
      id: 'branch-1',
      code: 'MAIN',
      name: 'Main Flagship',
      address: '100 Tech Park',
      phone: '9876543210',
      email: 'main@store.com',
      gstin: '29AAAAA0000A1Z5',
      isActive: true
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BranchesAdminComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        BranchesClient,
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

    branchesClient = TestBed.inject(BranchesClient);

    vi.spyOn(branchesClient, 'list').mockReturnValue(
      of(createFileResponse(mockBranches))
    );
    vi.spyOn(branchesClient, 'create').mockReturnValue(
      of(createFileResponse({ id: 'branch-2' }))
    );
    vi.spyOn(branchesClient, 'update').mockReturnValue(
      of(createFileResponse({}))
    );

    fixture = TestBed.createComponent(BranchesAdminComponent);
    component = fixture.componentInstance;
  });

  it('renders branches list and performs branch creation', async () => {
    fixture.detectChanges();
    await component.loadBranches();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.col-code')?.textContent).toContain('MAIN');
    expect(compiled.querySelector('.col-name')?.textContent).toContain('Main Flagship');

    // Create branch
    component.openCreateModal();
    component.formCode.set('EAST01');
    component.formName.set('East Branch');
    component.formAddress.set('50 Eastern Ring Rd');
    component.formPhone.set('9123456789');

    await component.saveBranch();

    expect(branchesClient.create).toHaveBeenCalledWith(
      expect.objectContaining({
        code: 'EAST01',
        name: 'East Branch',
        address: '50 Eastern Ring Rd',
        phone: '9123456789'
      })
    );
  });

  it('updates an existing branch', async () => {
    fixture.detectChanges();
    await component.loadBranches();
    fixture.detectChanges();

    const branch = component.branches()[0];
    component.openEditModal(branch);

    component.formName.set('Main Flagship Renamed');
    component.formIsActive.set(false);

    await component.saveBranch();

    expect(branchesClient.update).toHaveBeenCalledWith(
      branch.id,
      expect.objectContaining({
        name: 'Main Flagship Renamed',
        isActive: false
      })
    );
  });
});
