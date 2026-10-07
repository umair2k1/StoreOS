import { Injectable, signal, computed } from '@angular/core';

export interface BrandingState {
  tenantName: string;
  activeBranchName: string | null;
  logoUrl: string | null;
  themeColor: string;
}

@Injectable({ providedIn: 'root' })
export class BrandingSignalStore {
  private readonly state = signal<BrandingState>({
    tenantName: 'StoreOS', // internal fallback ONLY before onboarding/login populates real data -
                            // never shown to a real logged-in user of a fully onboarded tenant
    activeBranchName: null,
    logoUrl: null,
    themeColor: '#1e293b'
  });

  readonly tenantName = computed(() => this.state().tenantName);
  readonly activeBranchName = computed(() => this.state().activeBranchName);
  readonly logoUrl = computed(() => this.state().logoUrl);
  readonly themeColor = computed(() => this.state().themeColor);

  setBranding(data: Partial<BrandingState>): void {
    this.state.update(s => ({ ...s, ...data }));
  }
}
