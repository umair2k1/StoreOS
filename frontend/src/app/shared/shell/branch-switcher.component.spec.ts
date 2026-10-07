import { ComponentFixture, TestBed } from '@angular/core/testing';
import { BranchSwitcherComponent } from './branch-switcher.component';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BranchContextService } from '../../core/branch/branch-context.service';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

describe('BranchSwitcherComponent', () => {
  let fixture: ComponentFixture<BranchSwitcherComponent>;
  let authStore: AuthSignalStore;
  let branchContext: BranchContextService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BranchSwitcherComponent],
      providers: [
        AuthSignalStore,
        BranchContextService,
        BrandingSignalStore
      ]
    });

    authStore = TestBed.inject(AuthSignalStore);
    branchContext = TestBed.inject(BranchContextService);
    fixture = TestBed.createComponent(BranchSwitcherComponent);
  });

  it('is hidden for a single-branch user', () => {
    authStore.setAuthenticated({
      accessToken: 'tok',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'u1',
      displayName: 'Cashier',
      tenantId: 't1',
      allBranches: false,
      branchIds: ['b-1'],
      permissions: []
    });
    branchContext.setBranches([{ id: 'b-1', name: 'Single Branch' }]);
    branchContext.setCurrentBranchId('b-1');

    fixture.detectChanges();

    const switcher = fixture.nativeElement.querySelector('[data-testid="branch-switcher"]');
    expect(switcher).toBeNull();
  });

  it('is shown for a multi-branch user and selecting updates BranchContextService', () => {
    authStore.setAuthenticated({
      accessToken: 'tok',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'u1',
      displayName: 'Manager',
      tenantId: 't1',
      allBranches: false,
      branchIds: ['b-1', 'b-2'],
      permissions: []
    });
    branchContext.setBranches([
      { id: 'b-1', name: 'Downtown Branch' },
      { id: 'b-2', name: 'Uptown Branch' }
    ]);
    branchContext.setCurrentBranchId('b-1');

    fixture.detectChanges();

    const switcher = fixture.nativeElement.querySelector('[data-testid="branch-switcher"]');
    expect(switcher).toBeTruthy();

    const select = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    expect(select).toBeTruthy();
    expect(select.value).toBe('b-1');

    // Simulate switching branch to b-2
    select.value = 'b-2';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(branchContext.currentBranchId()).toBe('b-2');
  });
});
