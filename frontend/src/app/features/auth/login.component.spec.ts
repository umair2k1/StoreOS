import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { LoginComponent } from './login.component';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let httpMock: HttpTestingController;
  let authStore: AuthSignalStore;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        AuthSignalStore,
        BrandingSignalStore
      ]
    });

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    authStore = TestBed.inject(AuthSignalStore);
    router = TestBed.inject(Router);
    fixture.detectChanges();
  });

  afterEach(() => {
    localStorage.clear();
    httpMock.verify();
  });

  it('(a) submitting valid credentials populates AuthSignalStore and navigates to /pos', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    component.form.setValue({
      email: 'manager@apex.com',
      password: 'SecurePassword123!',
      tenantId: 'tenant-100'
    });
    expect(component.form.valid).toBe(true);

    component.submit();

    const req = httpMock.expectOne('/api/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    expect(req.request.body).toEqual({
      email: 'manager@apex.com',
      password: 'SecurePassword123!',
      tenantId: 'tenant-100'
    });

    req.flush({
      accessToken: 'jwt-access-token-123',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-001',
      displayName: 'Apex Manager',
      tenantId: 'tenant-100',
      allBranches: true,
      branchIds: ['b-1'],
      permissions: ['sales:create', 'pos:checkout']
    });

    expect(authStore.isAuthenticated()).toBe(true);
    expect(authStore.displayName()).toBe('Apex Manager');
    expect(authStore.tenantId()).toBe('tenant-100');
    expect(authStore.hasPermission('pos:checkout')).toBe(true);

    expect(navigateSpy).toHaveBeenCalledWith('/pos');
    expect(component.isSubmitting()).toBe(false);
  });

  it('(b) submitting invalid credentials displays error message and does not navigate', () => {
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    component.form.setValue({
      email: 'wrong@apex.com',
      password: 'BadPassword!',
      tenantId: ''
    });

    component.submit();

    const req = httpMock.expectOne('/api/auth/login');
    req.flush(
      { code: 'invalid_credentials', message: 'Invalid email or password.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    fixture.detectChanges();

    expect(authStore.isAuthenticated()).toBe(false);
    expect(component.errorMessage()).toBe('Invalid email or password.');
    expect(navigateSpy).not.toHaveBeenCalled();

    const errorEl = fixture.nativeElement.querySelector('.error-alert');
    expect(errorEl).toBeTruthy();
    expect(errorEl.textContent).toContain('Invalid email or password.');
  });

  it('(c) form is invalid when required fields are empty and submit does not make HTTP calls', () => {
    component.form.setValue({
      email: '',
      password: '',
      tenantId: ''
    });
    fixture.detectChanges();

    expect(component.form.invalid).toBe(true);

    const submitBtn = fixture.nativeElement.querySelector('.submit-btn') as HTMLButtonElement;
    expect(submitBtn.disabled).toBe(true);

    component.submit();
    httpMock.expectNone('/api/auth/login');
  });
});
