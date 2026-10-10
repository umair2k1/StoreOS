import { Injectable, effect, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { AuthSignalStore } from '../auth/auth-signal-store';
import { BrandingSignalStore } from './branding-signal-store';

interface PublicProfileDto {
  tenantId: string;
  name: string;
  subdomain: string;
  logoUrl?: string;
}

@Injectable({ providedIn: 'root' })
export class DynamicManifestService {
  private readonly document = inject(DOCUMENT);
  private readonly authStore = inject(AuthSignalStore);
  private readonly brandingStore = inject(BrandingSignalStore);
  private readonly http = inject(HttpClient);

  constructor() {
    effect(() => {
      const tenantId = this.authStore.tenantId();
      if (tenantId) {
        this.updateManifestHref(`/api/tenant/manifest.webmanifest?tenantId=${encodeURIComponent(tenantId)}`);
        this.loadBranding(tenantId);
      } else {
        this.updateManifestHref('/api/tenant/manifest.webmanifest');
        this.brandingStore.reset();
      }
    });
  }

  loadBranding(tenantId?: string | null): void {
    const url = tenantId
      ? `/api/tenant/public-profile?tenantId=${encodeURIComponent(tenantId)}`
      : '/api/tenant/public-profile';

    this.http.get<PublicProfileDto>(url).subscribe({
      next: (profile) => {
        if (profile?.name) {
          this.brandingStore.setBranding({
            tenantName: profile.name,
            logoUrl: profile.logoUrl ?? null
          });
        }
      },
      error: () => {
        // Fallback gracefully on network error or offline
      }
    });
  }

  updateManifestHref(href: string): void {
    const link = this.document.querySelector<HTMLLinkElement>('link[rel="manifest"]');
    if (link) {
      link.setAttribute('href', href);
    }
  }
}
