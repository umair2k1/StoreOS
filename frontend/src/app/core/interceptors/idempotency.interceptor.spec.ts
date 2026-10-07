import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { idempotencyInterceptor } from './idempotency.interceptor';

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

describe('idempotencyInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([idempotencyInterceptor])),
        provideHttpClientTesting()
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  const allowlistedEndpoints = [
    '/api/pos/checkout',
    '/api/pos/estimate/est-001/convert',
    '/api/sales/sale-002/payments',
    '/api/repair-jobs/rep-003/bill',
    '/api/tenant/import'
  ];

  for (const url of allowlistedEndpoints) {
    it(`attaches Idempotency-Key to POST ${url}`, () => {
      http.post<{ ok: boolean }>(url, { sample: true }).subscribe();

      const req = httpMock.expectOne(url);
      expect(req.request.method).toBe('POST');
      const key = req.request.headers.get('Idempotency-Key');
      expect(key).toBeTruthy();
      expect(UUID_REGEX.test(key!)).toBe(true);

      req.flush({ ok: true });
    });
  }

  it('does NOT attach Idempotency-Key to unlisted POST endpoints', () => {
    http.post<{ ok: boolean }>('/api/products', { name: 'Item' }).subscribe();
    http.post<{ ok: boolean }>('/api/customers', { name: 'Customer' }).subscribe();

    const req1 = httpMock.expectOne('/api/products');
    const req2 = httpMock.expectOne('/api/customers');

    expect(req1.request.headers.has('Idempotency-Key')).toBe(false);
    expect(req2.request.headers.has('Idempotency-Key')).toBe(false);

    req1.flush({ ok: true });
    req2.flush({ ok: true });
  });

  it('never attaches Idempotency-Key to GET requests, even on allowlisted URLs', () => {
    http.get<{ ok: boolean }>('/api/pos/checkout').subscribe();
    http.get<{ ok: boolean }>('/api/sales/sale-002/payments').subscribe();

    const req1 = httpMock.expectOne('/api/pos/checkout');
    const req2 = httpMock.expectOne('/api/sales/sale-002/payments');

    expect(req1.request.headers.has('Idempotency-Key')).toBe(false);
    expect(req2.request.headers.has('Idempotency-Key')).toBe(false);

    req1.flush({ ok: true });
    req2.flush({ ok: true });
  });
});
