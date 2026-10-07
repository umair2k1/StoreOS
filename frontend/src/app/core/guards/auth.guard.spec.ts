import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthSignalStore } from '../auth/auth-signal-store';

describe('authGuard', () => {
  let authStore: AuthSignalStore;
  let router: Router;

  const dummyRoute = {} as ActivatedRouteSnapshot;
  const dummyState = {} as RouterStateSnapshot;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        AuthSignalStore,
        provideRouter([])
      ]
    });

    authStore = TestBed.inject(AuthSignalStore);
    router = TestBed.inject(Router);
  });

  it('(a) allows route activation when user is authenticated', () => {
    authStore.setAuthenticated({
      accessToken: 'valid-token',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Manager',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['sales:create']
    });

    const result = TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState));
    expect(result).toBe(true);
  });

  it('(b) redirects to /login when user is unauthenticated', () => {
    authStore.clear();

    const result = TestBed.runInInjectionContext(() => authGuard(dummyRoute, dummyState));
    expect(result instanceof UrlTree).toBe(true);
    expect((result as UrlTree).toString()).toBe('/login');
  });
});
