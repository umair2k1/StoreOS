import { Injectable, signal, computed, inject } from '@angular/core';
import { BrandingSignalStore } from '../branding/branding-signal-store';

export interface BranchInfo {
  id: string;
  name: string;
}

@Injectable({ providedIn: 'root' })
export class BranchContextService {
  private readonly brandingStore = inject(BrandingSignalStore);

  private readonly _currentBranchId = signal<string | null>(null);
  private readonly _branches = signal<BranchInfo[]>([]);

  readonly currentBranchId = computed(() => this._currentBranchId());
  readonly branches = computed(() => this._branches());

  setBranches(branches: BranchInfo[]): void {
    this._branches.set(branches);
  }

  setCurrentBranchId(branchId: string | null, branchName?: string | null): void {
    this._currentBranchId.set(branchId);
    if (branchName !== undefined) {
      this.brandingStore.setBranding({ activeBranchName: branchName });
    } else if (branchId) {
      const match = this._branches().find(b => b.id === branchId);
      if (match) {
        this.brandingStore.setBranding({ activeBranchName: match.name });
      }
    } else {
      this.brandingStore.setBranding({ activeBranchName: null });
    }
  }
}
