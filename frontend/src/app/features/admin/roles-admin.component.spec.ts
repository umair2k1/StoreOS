import type { ComponentFixture } from '@angular/core/testing';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { RolesAdminComponent } from './roles-admin.component';
import type { RoleItem } from './roles-admin.component';
import { RolesClient } from '../../core/api/generated-client';
import type { FileResponse } from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import { PERMISSION_CODES } from '../../core/permissions/permission-catalog';

describe('RolesAdminComponent', () => {
  let fixture: ComponentFixture<RolesAdminComponent>;
  let component: RolesAdminComponent;
  let rolesClient: RolesClient;

  function createFileResponse(payload: unknown): FileResponse {
    const json = JSON.stringify(payload);
    return {
      data: new Blob([json], { type: 'application/json' }),
      status: 200,
      fileName: 'response.json',
      headers: {}
    } as unknown as FileResponse;
  }

  const mockRoles: RoleItem[] = [
    {
      id: 'role-1',
      name: 'Cashier',
      permissions: ['pos:checkout', 'sales:discount:apply']
    },
    {
      id: 'role-2',
      name: 'Inventory Auditor',
      permissions: ['inventory:manage', 'inventory:adjustments', 'inventory:cost:view']
    }
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RolesAdminComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        RolesClient,
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

    rolesClient = TestBed.inject(RolesClient);

    vi.spyOn(rolesClient, 'list').mockReturnValue(
      of(createFileResponse(mockRoles))
    );
    vi.spyOn(rolesClient, 'create').mockReturnValue(
      of(createFileResponse({ id: 'role-3' }))
    );

    fixture = TestBed.createComponent(RolesAdminComponent);
    component = fixture.componentInstance;
  });

  it('renders existing roles and lists their permission counts', async () => {
    fixture.detectChanges();
    await component.loadRoles();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.col-role-name')?.textContent).toContain('Cashier');
    expect(compiled.querySelector('.col-perm-count')?.textContent).toContain('2 / 17');
  });

  it('permission matrix UI displays all 17 fixed permission codes and submits selected subset', async () => {
    fixture.detectChanges();
    await component.loadRoles();
    fixture.detectChanges();

    component.openCreateModal();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;

    // Verify matrix grid has exactly 17 permission rows
    const matrixRows = compiled.querySelectorAll('.matrix-row');
    expect(matrixRows.length).toBe(17);

    // Verify all 17 catalog codes are present in the component definition
    expect(component.allCodes.length).toBe(17);
    expect(component.permissionDefinitions.length).toBe(17);
    for (const code of PERMISSION_CODES) {
      expect(component.allCodes).toContain(code);
    }

    // Set role name and select a specific subset of permissions
    component.newRoleName.set('Technician Lead');
    component.clearAllPermissions();
    component.togglePermission('repairs:manage');
    component.togglePermission('inventory:cost:view');
    component.togglePermission('pos:checkout');

    expect(component.selectedPermissionCodes().length).toBe(3);

    // Submit role creation
    await component.submitCreateRole();

    expect(rolesClient.create).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Technician Lead',
        permissionCodes: ['repairs:manage', 'inventory:cost:view', 'pos:checkout']
      })
    );
  });

  it('Select All selects all 17 permission codes', () => {
    component.openCreateModal();
    component.clearAllPermissions();
    expect(component.selectedPermissionCodes().length).toBe(0);

    component.selectAllPermissions();
    expect(component.selectedPermissionCodes().length).toBe(17);
  });
});
