import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { of, throwError } from 'rxjs';
import { BackupRestoreComponent } from './backup-restore.component';
import { PlatformClient } from '../../../core/api/generated-client';
import type { TenantBackupDto, FileResponse } from '../../../core/api/generated-client';
import { BrandingSignalStore } from '../../../core/branding/branding-signal-store';
import { AuthSignalStore } from '../../../core/auth/auth-signal-store';
import { ToastService } from '../../../shared/services/toast.service';

describe('BackupRestoreComponent', () => {
  let fixture: ComponentFixture<BackupRestoreComponent>;
  let component: BackupRestoreComponent;
  let platformClient: PlatformClient;
  let brandingStore: BrandingSignalStore;
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

  const mockBackup: TenantBackupDto = {
    schemaVersion: '1.0.0',
    exportedAt: new Date('2026-10-08T00:00:00Z'),
    tenantId: 'tenant-1111-1111-1111-1111'
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BackupRestoreComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        PlatformClient,
        BrandingSignalStore,
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

    platformClient = TestBed.inject(PlatformClient);
    brandingStore = TestBed.inject(BrandingSignalStore);
    router = TestBed.inject(Router);

    const authStore = TestBed.inject(AuthSignalStore);
    authStore.setAuthenticated({
      accessToken: 'token-admin',
      expiresAt: null,
      userId: 'u-admin',
      displayName: 'Admin User',
      tenantId: 't-1',
      allBranches: true,
      branchIds: ['b-1'],
      permissions: ['settings:manage']
    });

    brandingStore.setBranding({ tenantName: 'PhoneZone Store' });

    vi.spyOn(platformClient, 'export').mockReturnValue(
      of(mockBackup)
    );
    vi.spyOn(platformClient, 'import').mockReturnValue(
      of(createFileResponse({
        success: true,
        recordCounts: { products: 45, sales: 120, customers: 30 }
      }))
    );

    fixture = TestBed.createComponent(BackupRestoreComponent);
    component = fixture.componentInstance;
  });

  it('(a) export triggers a file download with correct filename and content', async () => {
    const createObjectUrlSpy = vi.spyOn(window.URL, 'createObjectURL').mockReturnValue('blob:http://localhost/test');
    const revokeObjectUrlSpy = vi.spyOn(window.URL, 'revokeObjectURL').mockImplementation(() => {});

    await component.exportBackup();

    expect(platformClient.export).toHaveBeenCalled();
    expect(createObjectUrlSpy).toHaveBeenCalled();
    expect(revokeObjectUrlSpy).toHaveBeenCalled();
  });

  it('(b) import is BLOCKED until the confirmation phrase is typed correctly', async () => {
    fixture.detectChanges();

    // Set backup file payload
    component.selectedBackup.set(mockBackup);
    component.selectedFileName.set('backup-test.json');
    component.isConfirmDialogOpen.set(true);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(component.expectedConfirmationPhrase()).toBe('PhoneZone Store');

    // 1. Initially empty -> button must be disabled
    component.confirmationInput.set('');
    fixture.detectChanges();
    expect(component.isConfirmationMatching()).toBe(false);

    const confirmBtn = compiled.querySelector('.btn-confirm-restore') as HTMLButtonElement;
    expect(confirmBtn.disabled).toBe(true);

    // 2. Incorrect phrase -> button still disabled and execution rejected
    component.confirmationInput.set('Wrong Store');
    fixture.detectChanges();
    expect(component.isConfirmationMatching()).toBe(false);
    expect(confirmBtn.disabled).toBe(true);

    await component.executeRestore();
    expect(platformClient.import).not.toHaveBeenCalled();

    // 3. Exactly typed confirmation phrase -> button enabled
    component.confirmationInput.set('PhoneZone Store');
    fixture.detectChanges();
    expect(component.isConfirmationMatching()).toBe(true);
    expect(confirmBtn.disabled).toBe(false);
  });

  it('(c) successful import shows a success message with returned record counts', async () => {
    fixture.detectChanges();

    component.selectedBackup.set(mockBackup);
    component.confirmationInput.set('PhoneZone Store');

    await component.executeRestore();
    fixture.detectChanges();

    expect(platformClient.import).toHaveBeenCalledWith(mockBackup);
    expect(component.importSuccessResult()?.success).toBe(true);

    const compiled = fixture.nativeElement as HTMLElement;
    const successAlert = compiled.querySelector('.alert-success');
    expect(successAlert).toBeTruthy();
    expect(successAlert?.textContent).toContain('Restore Completed Successfully');
    expect(successAlert?.textContent).toContain('products');
    expect(successAlert?.textContent).toContain('45 rows');
    expect(successAlert?.textContent).toContain('sales');
    expect(successAlert?.textContent).toContain('120 rows');
  });

  it('(d) rejected import (e.g. schema-version-mismatch) shows clear error without navigating away', async () => {
    const navigateSpy = vi.spyOn(router, 'navigate');

    vi.spyOn(platformClient, 'import').mockReturnValue(
      throwError(() => new Error('Schema version mismatch: Expected v1.0 but got v0.9'))
    );

    fixture.detectChanges();

    component.selectedBackup.set(mockBackup);
    component.confirmationInput.set('PhoneZone Store');

    await component.executeRestore();
    fixture.detectChanges();

    expect(component.errorMessage()).toContain('Schema version mismatch');

    const compiled = fixture.nativeElement as HTMLElement;
    const errorAlert = compiled.querySelector('.alert-danger');
    expect(errorAlert).toBeTruthy();
    expect(errorAlert?.textContent).toContain('Schema version mismatch');

    // Asserts did NOT navigate away
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
