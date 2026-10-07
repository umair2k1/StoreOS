import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { Observable } from 'rxjs';
import { onboardingGuard } from './onboarding.guard';
import { AuthSignalStore } from '../auth/auth-signal-store';

describe('onboardingGuard', () => {
  let authStore: AuthSignalStore;
  let router: Router;
  let httpMock: HttpTestingController;

  const dummyRoute = {} as ActivatedRouteSnapshot;
  const dummyState = {} as RouterStateSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthSignalStore,
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([])
      ]
    });

    authStore = TestBed.inject(AuthSignalStore);
    router = TestBed.inject(Router);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('(a) redirects admin with branches:manage to /onboarding when branch count is 0', () => {
    authStore.setAuthenticated({
      accessToken: 'token-admin',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'admin-1',
      displayName: 'Owner',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['branches:manage']
    });

    const guardResult = TestBed.runInInjectionContext(() => onboardingGuard(dummyRoute, dummyState));
    expect(guardResult instanceof Observable).toBe(true);

    let resolvedResult: boolean | UrlTree | undefined;
    (guardResult as Observable<boolean | UrlTree>).subscribe(result => {
      resolvedResult = result;
    });

    const req = httpMock.expectOne('/api/branches');
    expect(req.request.method).toBe('GET');
    req.flush([]);

    expect(resolvedResult instanceof UrlTree).toBe(true);
    expect((resolvedResult as UrlTree).toString()).toBe('/onboarding');
  });

  it('(b) redirects cashier without branches:manage to /awaiting-setup when branch count is 0', () => {
    authStore.setAuthenticated({
      accessToken: 'token-cashier',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'cashier-1',
      displayName: 'Cashier',
      tenantId: 'tenant-1',
      allBranches: false,
      branchIds: [],
      permissions: ['pos:checkout']
    });

    const guardResult = TestBed.runInInjectionContext(() => onboardingGuard(dummyRoute, dummyState));
    expect(guardResult instanceof Observable).toBe(true);

    let resolvedResult: boolean | UrlTree | undefined;
    (guardResult as Observable<boolean | UrlTree>).subscribe(result => {
      resolvedResult = result;
    });

    const req = httpMock.expectOne('/api/branches');
    expect(req.request.method).toBe('GET');
    req.flush([]);

    expect(resolvedResult instanceof UrlTree).toBe(true);
    expect((resolvedResult as UrlTree).toString()).toBe('/awaiting-setup');
  });

  it('(c) allows route activation when branch count is greater than 0', () => {
    authStore.setAuthenticated({
      accessToken: 'token-user',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Staff',
      tenantId: 'tenant-1',
      allBranches: false,
      branchIds: ['b1'],
      permissions: []
    });

    const guardResult = TestBed.runInInjectionContext(() => onboardingGuard(dummyRoute, dummyState));
    expect(guardResult instanceof Observable).toBe(true);

    let resolvedResult: boolean | UrlTree | undefined;
    (guardResult as Observable<boolean | UrlTree>).subscribe(result => {
      resolvedResult = result;
    });

    const req = httpMock.expectOne('/api/branches');
    expect(req.request.method).toBe('GET');
    req.flush([{ id: 'b1', code: 'MAIN', name: 'Main Branch' }]);

    expect(resolvedResult).toBe(true);
  });

  it('(d) redirects unauthenticated user to /login immediately without calling /api/branches', () => {
    authStore.clear();

    const guardResult = TestBed.runInInjectionContext(() => onboardingGuard(dummyRoute, dummyState));
    expect(guardResult instanceof UrlTree).toBe(true);
    expect((guardResult as UrlTree).toString()).toBe('/login');
    httpMock.expectNone('/api/branches');
  });
});
