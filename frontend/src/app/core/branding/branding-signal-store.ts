import { Injectable, signal, computed } from '@angular/core';

export interface BrandingState {
  tenantName: string;
  activeBranchName: string | null;
  logoUrl: string | null;
  themeColor: string;
}

const DEFAULT_STATE: BrandingState = {
  tenantName: 'StoreOS',
  activeBranchName: null,
  logoUrl: null,
  themeColor: '#1e293b'
};

const STORAGE_KEY = 'storeos_branding_state';

function normalizeLogoUrl(url: string | null | undefined): string | null {
  if (!url) return null;
  if (url.startsWith('storeos-media/')) {
    return '/api/tenant/logo.png?size=512';
  }
  return url;
}

function loadStoredState(): BrandingState {
  if (typeof window === 'undefined' || !window.localStorage) {
    return DEFAULT_STATE;
  }
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object' && parsed.tenantName) {
        return {
          ...DEFAULT_STATE,
          ...parsed,
          logoUrl: normalizeLogoUrl(parsed.logoUrl)
        };
      }
    }
  } catch {}
  return DEFAULT_STATE;
}

@Injectable({ providedIn: 'root' })
export class BrandingSignalStore {
  private readonly state = signal<BrandingState>(loadStoredState());

  readonly tenantName = computed(() => this.state().tenantName);
  readonly activeBranchName = computed(() => this.state().activeBranchName);
  readonly logoUrl = computed(() => this.state().logoUrl);
  readonly themeColor = computed(() => this.state().themeColor);

  setBranding(data: Partial<BrandingState>): void {
    if (data.logoUrl !== undefined) {
      data.logoUrl = normalizeLogoUrl(data.logoUrl);
    }
    this.state.update(s => {
      const next = { ...s, ...data };
      try {
        if (typeof window !== 'undefined' && window.localStorage) {
          localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
        }
      } catch {}
      return next;
    });
  }

  reset(): void {
    this.state.set(DEFAULT_STATE);
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.removeItem(STORAGE_KEY);
      }
    } catch {}
  }
}
