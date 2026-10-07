import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { correlationIdInterceptor, CorrelatedError } from './correlation-id.interceptor';

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

describe('correlationIdInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([correlationIdInterceptor])),
        provideHttpClientTesting()
      ]
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('attaches a valid UUID X-Correlation-ID header to every outgoing request', () => {
    http.get<{ ok: boolean }>('/api/test-1').subscribe();
    http.post<{ ok: boolean }>('/api/test-2', {}).subscribe();

    const req1 = httpMock.expectOne('/api/test-1');
    const req2 = httpMock.expectOne('/api/test-2');

    const correlationId1 = req1.request.headers.get('X-Correlation-ID');
    const correlationId2 = req2.request.headers.get('X-Correlation-ID');

    expect(correlationId1).toBeTruthy();
    expect(correlationId2).toBeTruthy();
    expect(UUID_REGEX.test(correlationId1!)).toBe(true);
    expect(UUID_REGEX.test(correlationId2!)).toBe(true);
    expect(correlationId1).not.toBe(correlationId2);

    req1.flush({ ok: true });
    req2.flush({ ok: true });
  });

  it('attaches the correlation ID to the thrown error object on failure', () => {
    let thrownError: unknown = null;

    http.get<{ ok: boolean }>('/api/error-test').subscribe({
      next: () => {},
      error: (err: unknown) => {
        thrownError = err;
      }
    });

    const req = httpMock.expectOne('/api/error-test');
    const expectedCorrelationId = req.request.headers.get('X-Correlation-ID');
    expect(expectedCorrelationId).toBeTruthy();

    req.flush({ message: 'Internal error' }, { status: 500, statusText: 'Server Error' });

    expect(thrownError).toBeTruthy();
    expect(thrownError instanceof HttpErrorResponse).toBe(true);
    const correlated = thrownError as CorrelatedError;
    expect(correlated.correlationId).toBe(expectedCorrelationId);
  });
});
