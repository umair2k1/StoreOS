import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { LogoUploaderComponent } from './logo-uploader.component';

describe('LogoUploaderComponent', () => {
  let fixture: ComponentFixture<LogoUploaderComponent>;
  let component: LogoUploaderComponent;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [LogoUploaderComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });

    fixture = TestBed.createComponent(LogoUploaderComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('uploads a file using multipart form data and updates preview URL', () => {
    let emittedUrl: string | null = null;
    component.logoUploaded.subscribe(url => {
      emittedUrl = url;
    });

    const file = new File(['valid png data'], 'test-logo.png', { type: 'image/png' });
    component.upload(file);

    const req = httpMock.expectOne('/api/tenants/me/logo');
    expect(req.request.method).toBe('POST');
    expect(req.request.body instanceof FormData).toBe(true);

    const expectedUrl = 'https://media.storeos.io/tenants/t1/logo.webp';
    req.flush({ logoUrl: expectedUrl });

    expect(component.isUploading()).toBe(false);
    expect(component.previewUrl()).toBe(expectedUrl);
    expect(emittedUrl).toBe(expectedUrl);
    expect(component.errorMessage()).toBeNull();
  });

  it('displays error message to user when upload is rejected (422 response)', () => {
    const file = new File(['<svg></svg>'], 'test-logo.svg', { type: 'image/svg+xml' });
    component.upload(file);

    const req = httpMock.expectOne('/api/tenants/me/logo');
    expect(req.request.method).toBe('POST');

    const errorPayload = {
      code: 'logo_invalid_type',
      message: 'Only PNG, JPEG, and WebP images are allowed.'
    };
    req.flush(errorPayload, { status: 422, statusText: 'Unprocessable Entity' });
    fixture.detectChanges();

    expect(component.isUploading()).toBe(false);
    expect(component.errorMessage()).toBe('Only PNG, JPEG, and WebP images are allowed.');

    const errorEl = fixture.nativeElement.querySelector('.error-message');
    expect(errorEl).toBeTruthy();
    expect(errorEl.textContent).toContain('Only PNG, JPEG, and WebP images are allowed.');
  });
});
