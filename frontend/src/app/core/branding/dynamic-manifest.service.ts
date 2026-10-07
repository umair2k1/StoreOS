import { Injectable, effect, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { AuthSignalStore } from '../auth/auth-signal-store';

@Injectable({ providedIn: 'root' })
export class DynamicManifestService {
  private readonly document = inject(DOCUMENT);
  private readonly authStore = inject(AuthSignalStore);

  constructor() {
    effect(() => {
      const tenantId = this.authStore.tenantId();
      if (tenantId) {
        this.updateManifestHref(`/api/tenant/manifest.webmanifest?tenantId=${encodeURIComponent(tenantId)}`);
      } else {
        this.updateManifestHref('/api/tenant/manifest.webmanifest');
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
