import { Injectable, signal, computed } from '@angular/core';

export interface AuthState {
  accessToken: string | null;
  expiresAt: string | null;
  userId: string | null;
  displayName: string | null;
  tenantId: string | null;
  allBranches: boolean;
  branchIds: string[];
  permissions: string[];
}

const EMPTY_STATE: AuthState = {
  accessToken: null,
  expiresAt: null,
  userId: null,
  displayName: null,
  tenantId: null,
  allBranches: false,
  branchIds: [],
  permissions: []
};
const STORAGE_KEY = 'storeos_auth_state';

function loadStoredState(): AuthState {
  if (typeof window === 'undefined' || !window.localStorage) {
    return EMPTY_STATE;
  }
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed && typeof parsed === 'object' && parsed.accessToken) {
        return parsed as AuthState;
      }
    }
  } catch {}
  return EMPTY_STATE;
}

@Injectable({ providedIn: 'root' })
export class AuthSignalStore {
  private readonly state = signal<AuthState>(loadStoredState());

  readonly isAuthenticated = computed(() => this.state().accessToken !== null);
  readonly accessToken = computed(() => this.state().accessToken);
  readonly displayName = computed(() => this.state().displayName);
  readonly permissions = computed(() => this.state().permissions);
  readonly branchIds = computed(() => this.state().branchIds);
  readonly allBranches = computed(() => this.state().allBranches);
  readonly userId = computed(() => this.state().userId);
  readonly tenantId = computed(() => this.state().tenantId);

  setAuthenticated(data: Omit<AuthState, never>): void {
    this.state.set({ ...data });
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(data));
      }
    } catch {}
  }

  clear(): void {
    this.state.set(EMPTY_STATE);
    try {
      if (typeof window !== 'undefined' && window.localStorage) {
        localStorage.removeItem(STORAGE_KEY);
      }
    } catch {}
  }
  hasPermission(code: string): boolean {
    return this.state().permissions.includes(code);
  }
}
