import { TestBed } from '@angular/core/testing';
import { DOCUMENT } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { DynamicManifestService } from './dynamic-manifest.service';
import { AuthSignalStore } from '../auth/auth-signal-store';
import { BrandingSignalStore } from './branding-signal-store';

describe('DynamicManifestService', () => {
  let authStore: AuthSignalStore;
  let brandingStore: BrandingSignalStore;
  let service: DynamicManifestService;
  let httpMock: HttpTestingController;
  let doc: Document;
  let manifestLink: HTMLLinkElement;

  beforeEach(() => {
    doc = document;
    // Ensure test <link rel="manifest"> exists
    let existingLink = doc.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    if (!existingLink) {
      manifestLink = doc.createElement('link');
      manifestLink.setAttribute('rel', 'manifest');
      manifestLink.setAttribute('href', '/api/tenant/manifest.webmanifest');
      doc.head.appendChild(manifestLink);
    } else {
      manifestLink = existingLink;
      manifestLink.setAttribute('href', '/api/tenant/manifest.webmanifest');
    }

    TestBed.configureTestingModule({
      providers: [
        AuthSignalStore,
        BrandingSignalStore,
        DynamicManifestService,
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: DOCUMENT, useValue: doc }
      ]
    });

    authStore = TestBed.inject(AuthSignalStore);
    brandingStore = TestBed.inject(BrandingSignalStore);
    httpMock = TestBed.inject(HttpTestingController);
    service = TestBed.inject(DynamicManifestService);
  });

  afterEach(() => {
    // Reset href
    manifestLink.setAttribute('href', '/api/tenant/manifest.webmanifest');
  });

  it('updates the DOM manifest link href when tenantId is set via setAuthenticated', () => {
    authStore.setAuthenticated({
      accessToken: 'token-abc',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Shop Manager',
      tenantId: 'tenant-apex-123',
      allBranches: true,
      branchIds: [],
      permissions: []
    });

    TestBed.flushEffects();

    const link = doc.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('/api/tenant/manifest.webmanifest?tenantId=tenant-apex-123');

    const req = httpMock.expectOne('/api/tenant/public-profile?tenantId=tenant-apex-123');
    req.flush({ tenantId: 'tenant-apex-123', name: 'Phone Zone', subdomain: 'phonezone' });
    expect(brandingStore.tenantName()).toBe('Phone Zone');
  });

  it('reverts the DOM manifest link href when session is cleared', () => {
    authStore.setAuthenticated({
      accessToken: 'token-abc',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Shop Manager',
      tenantId: 'tenant-apex-123',
      allBranches: true,
      branchIds: [],
      permissions: []
    });
    TestBed.flushEffects();

    const req = httpMock.expectOne('/api/tenant/public-profile?tenantId=tenant-apex-123');
    req.flush({ tenantId: 'tenant-apex-123', name: 'Apex Electronics', subdomain: 'apex' });

    authStore.clear();
    TestBed.flushEffects();

    const link = doc.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    expect(link?.getAttribute('href')).toBe('/api/tenant/manifest.webmanifest');
    expect(brandingStore.tenantName()).toBe('StoreOS');
  });
});
