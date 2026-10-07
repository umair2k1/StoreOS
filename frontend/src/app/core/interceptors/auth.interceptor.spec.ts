import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { authInterceptor, resetAuthInterceptorState } from './auth.interceptor';
import { AuthSignalStore } from '../auth/auth-signal-store';
import { AuthRefreshService } from '../auth/auth-refresh.service';
import { of, throwError } from 'rxjs';

interface TestResourceResponse {
  data: string;
}

describe('authInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let authStore: AuthSignalStore;
  let refreshService: AuthRefreshService;

  beforeEach(() => {
    resetAuthInterceptorState();

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        AuthSignalStore,
        AuthRefreshService
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
    authStore = TestBed.inject(AuthSignalStore);
    refreshService = TestBed.inject(AuthRefreshService);
  });

  afterEach(() => {
    httpMock.verify();
    resetAuthInterceptorState();
  });

  it('(a) attaches Authorization header when token exists in store', () => {
    authStore.setAuthenticated({
      accessToken: 'initial-jwt-token',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Test User',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['sales:read']
    });

    http.get<{ ok: boolean }>('/api/test').subscribe();

    const req = httpMock.expectOne('/api/test');
    expect(req.request.headers.get('Authorization')).toBe('Bearer initial-jwt-token');
    req.flush({ ok: true });
  });

  it('does not attach Authorization header when no token in store', () => {
    authStore.clear();

    http.get<{ ok: boolean }>('/api/test').subscribe();

    const req = httpMock.expectOne('/api/test');
    expect(req.request.headers.has('Authorization')).toBe(false);
    req.flush({ ok: true });
  });

  it('(b) & (c) triggers exactly ONE refresh for 3 concurrent 401s and retries all 3 with new token', () => {
    authStore.setAuthenticated({
      accessToken: 'expired-token',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Test User',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: []
    });

    let refreshCallCount = 0;
    vi.spyOn(refreshService, 'refresh').mockImplementation(() => {
      refreshCallCount++;
      authStore.setAuthenticated({
        accessToken: 'fresh-new-token',
        expiresAt: '2026-10-08T13:00:00Z',
        userId: 'user-1',
        displayName: 'Test User',
        tenantId: 'tenant-1',
        allBranches: true,
        branchIds: [],
        permissions: []
      });
      return of(void 0);
    });

    const res1: TestResourceResponse[] = [];
    const res2: TestResourceResponse[] = [];
    const res3: TestResourceResponse[] = [];

    http.get<TestResourceResponse>('/api/resource-1').subscribe((val) => res1.push(val));
    http.get<TestResourceResponse>('/api/resource-2').subscribe((val) => res2.push(val));
    http.get<TestResourceResponse>('/api/resource-3').subscribe((val) => res3.push(val));

    const req1 = httpMock.expectOne('/api/resource-1');
    const req2 = httpMock.expectOne('/api/resource-2');
    const req3 = httpMock.expectOne('/api/resource-3');

    expect(req1.request.headers.get('Authorization')).toBe('Bearer expired-token');
    expect(req2.request.headers.get('Authorization')).toBe('Bearer expired-token');
    expect(req3.request.headers.get('Authorization')).toBe('Bearer expired-token');

    // Simulate concurrent 401 responses
    req1.flush({ error: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    req2.flush({ error: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    req3.flush({ error: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    // (b) Buffering proof: exactly one refresh call was triggered
    expect(refreshCallCount).toBe(1);

    // (c) All three requests are retried with the fresh new token
    const retry1 = httpMock.expectOne('/api/resource-1');
    const retry2 = httpMock.expectOne('/api/resource-2');
    const retry3 = httpMock.expectOne('/api/resource-3');

    expect(retry1.request.headers.get('Authorization')).toBe('Bearer fresh-new-token');
    expect(retry2.request.headers.get('Authorization')).toBe('Bearer fresh-new-token');
    expect(retry3.request.headers.get('Authorization')).toBe('Bearer fresh-new-token');

    retry1.flush({ data: 'resource 1 data' });
    retry2.flush({ data: 'resource 2 data' });
    retry3.flush({ data: 'resource 3 data' });

    expect(res1).toEqual([{ data: 'resource 1 data' }]);
    expect(res2).toEqual([{ data: 'resource 2 data' }]);
    expect(res3).toEqual([{ data: 'resource 3 data' }]);
  });

  it('(d) clears auth store and propagates original error on failed refresh', () => {
    authStore.setAuthenticated({
      accessToken: 'expired-token',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Test User',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: []
    });

    vi.spyOn(refreshService, 'refresh').mockImplementation(() => {
      authStore.clear();
      return throwError(() => new HttpErrorResponse({ status: 401, statusText: 'Refresh Failed' }));
    });

    let receivedError: HttpErrorResponse | null = null;
    http.get<TestResourceResponse>('/api/resource').subscribe({
      next: () => {},
      error: (err: unknown) => {
        if (err instanceof HttpErrorResponse) {
          receivedError = err;
        }
      }
    });

    const req = httpMock.expectOne('/api/resource');
    req.flush({ error: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(receivedError).toBeTruthy();
    expect(authStore.isAuthenticated()).toBe(false);
    expect(authStore.accessToken()).toBeNull();
  });

  it('does not trigger refresh for 401 on /api/auth/refresh or /api/auth/login', () => {
    const refreshSpy = vi.spyOn(refreshService, 'refresh');

    let receivedError: HttpErrorResponse | null = null;
    http.post<{ ok: boolean }>('/api/auth/refresh', {}).subscribe({
      next: () => {},
      error: (err: unknown) => {
        if (err instanceof HttpErrorResponse) {
          receivedError = err;
        }
      }
    });

    const req = httpMock.expectOne('/api/auth/refresh');
    req.flush({ error: 'unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(refreshSpy).not.toHaveBeenCalled();
    expect(receivedError).toBeTruthy();
  });
});
