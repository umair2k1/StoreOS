import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { LockScreenComponent } from './lock-screen.component';
import { LockScreenOverlayService } from './lock-screen-overlay.service';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

describe('LockScreenComponent', () => {
  let fixture: ComponentFixture<LockScreenComponent>;
  let component: LockScreenComponent;
  let httpMock: HttpTestingController;
  let authStore: AuthSignalStore;
  let lockOverlay: LockScreenOverlayService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LockScreenComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        AuthSignalStore,
        BrandingSignalStore,
        LockScreenOverlayService
      ]
    });

    fixture = TestBed.createComponent(LockScreenComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    authStore = TestBed.inject(AuthSignalStore);
    lockOverlay = TestBed.inject(LockScreenOverlayService);

    authStore.setAuthenticated({
      accessToken: 'old-token',
      expiresAt: '2026-10-08T10:00:00Z',
      userId: 'user-001',
      displayName: 'Store Manager',
      tenantId: 'tenant-123',
      allBranches: true,
      branchIds: [],
      permissions: ['pos:checkout']
    });

    lockOverlay.show();
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('re-authenticates successfully with correct password and closes overlay', () => {
    component.form.setValue({ password: 'CorrectPassword123!' });
    component.submit();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.body).toEqual({
      email: 'Store Manager',
      password: 'CorrectPassword123!',
      tenantId: 'tenant-123'
    });

    req.flush({
      accessToken: 'new-token-456',
      expiresAt: '2026-10-08T14:00:00Z',
      userId: 'user-001',
      displayName: 'Store Manager',
      tenantId: 'tenant-123',
      allBranches: true,
      branchIds: [],
      permissions: ['pos:checkout']
    });

    expect(authStore.accessToken()).toBe('new-token-456');
    expect(lockOverlay.isVisible()).toBe(false);
    expect(component.isSubmitting()).toBe(false);
  });

  it('displays error message on wrong password and keeps overlay visible', () => {
    component.form.setValue({ password: 'WrongPassword!' });
    component.submit();

    const req = httpMock.expectOne('/api/auth/login');
    req.flush(
      { code: 'invalid_credentials', message: 'Incorrect password.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    fixture.detectChanges();

    expect(component.errorMessage()).toBe('Incorrect password.');
    expect(lockOverlay.isVisible()).toBe(true);

    const errorEl = fixture.nativeElement.querySelector('.error-alert');
    expect(errorEl).toBeTruthy();
    expect(errorEl.textContent).toContain('Incorrect password.');
  });
});
