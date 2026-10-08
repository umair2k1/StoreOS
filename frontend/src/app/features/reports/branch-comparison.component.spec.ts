import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { BranchComparisonComponent } from './branch-comparison.component';
import type { BranchComparisonData } from './branch-comparison.component';
import { ReportsClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { ToastService } from '../../shared/services/toast.service';

describe('BranchComparisonComponent', () => {
  let fixture: ComponentFixture<BranchComparisonComponent>;
  let component: BranchComparisonComponent;
  let reportsClient: ReportsClient;
  let authStore: AuthSignalStore;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockComparison: BranchComparisonData = {
    branches: [
      {
        branchId: 'b1',
        branchName: 'Downtown Store',
        revenue: 50000,
        invoiceCount: 10,
        profit: 15000,
        activeRepairs: 4
      },
      {
        branchId: 'b2',
        branchName: 'Uptown Store',
        revenue: 35000,
        invoiceCount: 7,
        profit: 10000,
        activeRepairs: 2
      }
    ],
    consolidated: {
      branchId: '00000000-0000-0000-0000-000000000000',
      branchName: 'Consolidated (Total)',
      revenue: 85000,
      invoiceCount: 17,
      profit: 25000,
      activeRepairs: 6
    }
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BranchComparisonComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        ReportsClient,
        AuthSignalStore,
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

    reportsClient = TestBed.inject(ReportsClient);
    authStore = TestBed.inject(AuthSignalStore);

    vi.spyOn(reportsClient, 'getBranchComparison').mockReturnValue(
      of(createFileResponse(mockComparison))
    );
  });

  it('renders comparison matrix for multi-branch user (allBranches=true or branchIds.length > 1)', async () => {
    // Configure multi-branch user
    authStore.setAuthenticated({
      accessToken: 'token-123',
      expiresAt: null,
      userId: 'u1',
      displayName: 'Manager',
      tenantId: 't1',
      allBranches: true,
      branchIds: ['b1', 'b2'],
      permissions: ['reports:view']
    });

    fixture = TestBed.createComponent(BranchComparisonComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();
    await component.loadComparison();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.isMultiBranch()).toBe(true);

    const container = compiled.querySelector('.branch-comparison-container');
    expect(container).toBeTruthy();

    const rows = compiled.querySelectorAll('.branch-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Downtown Store');
    expect(rows[1].textContent).toContain('Uptown Store');

    const consolidated = compiled.querySelector('.consolidated-row');
    expect(consolidated).toBeTruthy();
    expect(consolidated?.textContent).toContain('85,000');
  });

  it('HARD CONSTRAINT: branch comparison is ENTIRELY HIDDEN for a single-branch user', async () => {
    // Configure single-branch user
    authStore.setAuthenticated({
      accessToken: 'token-123',
      expiresAt: null,
      userId: 'u1',
      displayName: 'Cashier',
      tenantId: 't1',
      allBranches: false,
      branchIds: ['b1'],
      permissions: ['reports:view']
    });

    fixture = TestBed.createComponent(BranchComparisonComponent);
    component = fixture.componentInstance;

    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.isMultiBranch()).toBe(false);

    // Entire comparison container must be hidden/absent from DOM
    expect(compiled.querySelector('.branch-comparison-container')).toBeNull();
    expect(compiled.querySelector('.comparison-table')).toBeNull();
    expect(reportsClient.getBranchComparison).not.toHaveBeenCalled();
  });
});
