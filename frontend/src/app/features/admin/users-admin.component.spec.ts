import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { UsersAdminComponent } from './users-admin.component';
import type { UserItem, RoleOption, BranchOption } from './users-admin.component';
import { UsersClient, RolesClient, BranchesClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

describe('UsersAdminComponent', () => {
  let fixture: ComponentFixture<UsersAdminComponent>;
  let component: UsersAdminComponent;
  let usersClient: UsersClient;
  let rolesClient: RolesClient;
  let branchesClient: BranchesClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockUsers: UserItem[] = [
    {
      id: 'user-1',
      email: 'alex@store.com',
      displayName: 'Alex Smith',
      isActive: true,
      tokenVersion: 1,
      roles: [{ roleId: 'role-1', roleName: 'Cashier' }],
      branches: [{ branchId: 'b-1', branchName: 'Main Store', branchCode: 'MAIN', isDefault: true }]
    }
  ];

  const mockRoles: RoleOption[] = [
    { id: 'role-1', name: 'Cashier' },
    { id: 'role-2', name: 'Manager' }
  ];

  const mockBranches: BranchOption[] = [
    { id: 'b-1', name: 'Main Store', code: 'MAIN' }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [UsersAdminComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        UsersClient,
        RolesClient,
        BranchesClient,
        {
          provide: ToastService,
          useValue: {
            success: vi.fn(),
            error: vi.fn(),
            info: vi.fn(),
            warning: vi.fn()
          }
        }
      ]
    }).compileComponents();

    usersClient = TestBed.inject(UsersClient);
    rolesClient = TestBed.inject(RolesClient);
    branchesClient = TestBed.inject(BranchesClient);

    vi.spyOn(usersClient, 'list').mockReturnValue(
      of(createFileResponse(mockUsers))
    );
    vi.spyOn(rolesClient, 'list').mockReturnValue(
      of(createFileResponse(mockRoles))
    );
    vi.spyOn(branchesClient, 'list').mockReturnValue(
      of(createFileResponse(mockBranches))
    );
    vi.spyOn(usersClient, 'create').mockReturnValue(
      of(createFileResponse({ id: 'user-2' }))
    );
    vi.spyOn(usersClient, 'deactivate').mockReturnValue(
      of(createFileResponse({}))
    );
    vi.spyOn(usersClient, 'resetPassword').mockReturnValue(
      of(createFileResponse({}))
    );

    fixture = TestBed.createComponent(UsersAdminComponent);
    component = fixture.componentInstance;
  });

  it('renders users list and handles user creation with initial role and branch', async () => {
    fixture.detectChanges();
    await component.initData();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.col-name')?.textContent).toContain('Alex Smith');
    expect(compiled.querySelector('.col-email')?.textContent).toContain('alex@store.com');
    expect(compiled.querySelector('.badge-role')?.textContent).toContain('Cashier');

    // Create user
    component.openCreateModal();
    component.newDisplayName.set('Deepa Nair');
    component.newEmail.set('deepa@store.com');
    component.newPassword.set('DeepaSecret99#');
    component.newRoleId.set('role-2');
    component.newBranchId.set('b-1');

    await component.submitCreateUser();

    expect(usersClient.create).toHaveBeenCalledWith(
      expect.objectContaining({
        displayName: 'Deepa Nair',
        email: 'deepa@store.com',
        password: 'DeepaSecret99#',
        initialRoleId: 'role-2',
        initialBranchId: 'b-1'
      })
    );
  });

  it('deactivating a user calls endpoint and reflects new state in the list', async () => {
    fixture.detectChanges();
    await component.initData();
    fixture.detectChanges();

    const user = component.users()[0];
    expect(user.isActive).toBe(true);

    await component.deactivateUser(user);
    fixture.detectChanges();

    expect(usersClient.deactivate).toHaveBeenCalledWith('user-1');

    // Reflects inactive state in list
    const updatedUser = component.users().find(u => u.id === 'user-1');
    expect(updatedUser?.isActive).toBe(false);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.col-status')?.textContent).toContain('Deactivated');
  });

  it('resets user password via temporary password modal', async () => {
    fixture.detectChanges();
    await component.initData();
    fixture.detectChanges();

    const user = component.users()[0];
    component.openResetPasswordModal(user);
    fixture.detectChanges();

    expect(component.isResetModalOpen()).toBe(true);

    component.tempPassword.set('TempSecret123!');
    await component.submitResetPassword();

    expect(usersClient.resetPassword).toHaveBeenCalledWith(
      'user-1',
      { newTemporaryPassword: 'TempSecret123!' }
    );
    expect(component.isResetModalOpen()).toBe(false);
  });
});
