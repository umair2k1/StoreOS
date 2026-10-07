import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PermissionDirective } from './permission.directive';
import { AuthSignalStore } from '../auth/auth-signal-store';

@Component({
  standalone: true,
  imports: [PermissionDirective],
  template: `
    <div id="container">
      <span *pzCan="'sales:create'" id="sale-button">Create Sale</span>
      <span *pzCan="'admin:manage'" id="admin-panel">Admin Panel</span>
    </div>
  `
})
class TestHostComponent {}

describe('PermissionDirective (*pzCan)', () => {
  let fixture: ComponentFixture<TestHostComponent>;
  let authStore: AuthSignalStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TestHostComponent, PermissionDirective],
      providers: [AuthSignalStore]
    });

    authStore = TestBed.inject(AuthSignalStore);
    authStore.clear();
    fixture = TestBed.createComponent(TestHostComponent);
  });

  it('(a) renders the embedded template when the user has the required permission', () => {
    authStore.setAuthenticated({
      accessToken: 'token-1',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Sales Rep',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['sales:create']
    });

    fixture.detectChanges();

    const saleButton = fixture.nativeElement.querySelector('#sale-button');
    expect(saleButton).toBeTruthy();
    expect(saleButton.textContent.trim()).toBe('Create Sale');
  });

  it('(b) does not render the template when the user lacks the required permission', () => {
    authStore.setAuthenticated({
      accessToken: 'token-1',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-1',
      displayName: 'Sales Rep',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['sales:create']
    });

    fixture.detectChanges();

    const adminPanel = fixture.nativeElement.querySelector('#admin-panel');
    expect(adminPanel).toBeNull();
  });

  it('(c) reacts dynamically to live changes in the permissions signal', () => {
    authStore.clear();
    fixture.detectChanges();

    // Initially neither element is visible
    expect(fixture.nativeElement.querySelector('#sale-button')).toBeNull();
    expect(fixture.nativeElement.querySelector('#admin-panel')).toBeNull();

    // Dynamically grant admin:manage permission mid-test
    authStore.setAuthenticated({
      accessToken: 'token-2',
      expiresAt: '2026-10-08T12:00:00Z',
      userId: 'user-2',
      displayName: 'Administrator',
      tenantId: 'tenant-1',
      allBranches: true,
      branchIds: [],
      permissions: ['admin:manage']
    });

    fixture.detectChanges();

    // View reactively appears without a page reload
    const adminPanel = fixture.nativeElement.querySelector('#admin-panel');
    expect(adminPanel).toBeTruthy();
    expect(adminPanel.textContent.trim()).toBe('Admin Panel');
    expect(fixture.nativeElement.querySelector('#sale-button')).toBeNull();

    // Revoke permission dynamically
    authStore.clear();
    fixture.detectChanges();

    // View reactively disappears
    expect(fixture.nativeElement.querySelector('#admin-panel')).toBeNull();
  });
});
