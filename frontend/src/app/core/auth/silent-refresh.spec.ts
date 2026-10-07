import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ApplicationInitStatus, provideAppInitializer } from '@angular/core';
import { provideRouter, Router, RouterOutlet } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { silentRefreshInitializer } from './silent-refresh';
import { AuthSignalStore } from './auth-signal-store';
import { AuthRefreshService } from './auth-refresh.service';
import { Subject, of, throwError } from 'rxjs';

@Component({
  standalone: true,
  template: '<h1>Home Component</h1>'
})
class DummyHomeComponent {
  static activatedCount = 0;
  constructor() {
    DummyHomeComponent.activatedCount++;
  }
}

@Component({
  standalone: true,
  imports: [RouterOutlet],
  template: '<router-outlet></router-outlet>'
})
class TestRootComponent {}

describe('silentRefreshInitializer', () => {
  describe('unit execution of initializer', () => {
    let authStore: AuthSignalStore;
    let refreshService: AuthRefreshService;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          AuthSignalStore,
          AuthRefreshService
        ]
      });
      authStore = TestBed.inject(AuthSignalStore);
      refreshService = TestBed.inject(AuthRefreshService);
    });

    it('(a) sets isAuthenticated to true when refresh succeeds', async () => {
      authStore.clear();

      vi.spyOn(refreshService, 'refresh').mockImplementation(() => {
        authStore.setAuthenticated({
          accessToken: 'fresh-cookie-token',
          expiresAt: '2026-10-08T14:00:00Z',
          userId: 'user-42',
          displayName: 'Test Operator',
          tenantId: 'tenant-42',
          allBranches: true,
          branchIds: [],
          permissions: ['pos:operate']
        });
        return of(void 0);
      });

      const initFn = TestBed.runInInjectionContext(() => silentRefreshInitializer());
      await initFn();

      expect(authStore.isAuthenticated()).toBe(true);
      expect(authStore.accessToken()).toBe('fresh-cookie-token');
      expect(authStore.displayName()).toBe('Test Operator');
    });

    it('(b) resolves without throwing when refresh returns 401, leaving isAuthenticated false', async () => {
      authStore.clear();

      vi.spyOn(refreshService, 'refresh').mockImplementation(() =>
        throwError(() => ({ status: 401, message: 'No cookie' }))
      );

      const initFn = TestBed.runInInjectionContext(() => silentRefreshInitializer());

      // Must resolve cleanly, never throw
      await expect(initFn()).resolves.toBeUndefined();

      expect(authStore.isAuthenticated()).toBe(false);
      expect(authStore.accessToken()).toBeNull();
    });
  });

  describe('(c) router harness gating on initializer completion', () => {
    beforeEach(() => {
      DummyHomeComponent.activatedCount = 0;
    });

    it('holds up router activation until the silent refresh promise resolves', async () => {
      const refreshSubject = new Subject<void>();
      const mockRefreshService = {
        refresh: () => refreshSubject.asObservable()
      };

      TestBed.configureTestingModule({
        providers: [
          AuthSignalStore,
          { provide: AuthRefreshService, useValue: mockRefreshService },
          provideRouter([{ path: '', component: DummyHomeComponent }]),
          provideAppInitializer(() => {
            const initFn = silentRefreshInitializer();
            return initFn();
          })
        ]
      });

      const initStatus = TestBed.inject(ApplicationInitStatus);
      const router = TestBed.inject(Router);

      // Initial navigation has NOT completed and application is not initialized
      expect(initStatus.done).toBe(false);
      expect(DummyHomeComponent.activatedCount).toBe(0);

      // Now complete the slow refresh
      refreshSubject.next();
      refreshSubject.complete();

      // ApplicationInitStatus donePromise resolves
      await initStatus.donePromise;
      expect(initStatus.done).toBe(true);

      // Router now activates the root component and home component
      const fixture = TestBed.createComponent(TestRootComponent);
      await router.navigateByUrl('/');
      fixture.detectChanges();

      expect(DummyHomeComponent.activatedCount).toBe(1);
    });
  });
});
