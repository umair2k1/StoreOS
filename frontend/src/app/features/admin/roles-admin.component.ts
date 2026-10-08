import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { RolesClient } from '../../core/api/generated-client';
import type {
  CreateRoleCommand,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';
import {
  PERMISSION_DEFINITIONS,
  PERMISSION_CODES
} from '../../core/permissions/permission-catalog';
import type {
  PermissionCode,
  PermissionDefinition
} from '../../core/permissions/permission-catalog';

export interface RoleItem {
  id: string;
  name: string;
  permissions?: string[];
}

@Component({
  selector: 'app-roles-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './roles-admin.component.html',
  styleUrl: './roles-admin.component.scss'
})
export class RolesAdminComponent implements OnInit {
  private readonly rolesClient = inject(RolesClient);
  private readonly toastService = inject(ToastService);

  readonly permissionDefinitions: readonly PermissionDefinition[] = PERMISSION_DEFINITIONS;
  readonly allCodes: readonly PermissionCode[] = PERMISSION_CODES;

  readonly roles = signal<RoleItem[]>([]);
  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Modal / Create Role Form State
  readonly isCreateModalOpen = signal<boolean>(false);
  readonly isCreating = signal<boolean>(false);
  readonly newRoleName = signal<string>('');
  readonly selectedPermissionCodes = signal<string[]>([]);

  ngOnInit(): void {
    this.loadRoles();
  }

  async loadRoles(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      const resp = await firstValueFrom(this.rolesClient.list());
      const parsed = await this.parseFileResponse<RoleItem[]>(resp);
      this.roles.set(Array.isArray(parsed) ? parsed : []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load roles.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  openCreateModal(): void {
    this.newRoleName.set('');
    // Default with POS permissions pre-checked
    this.selectedPermissionCodes.set(['pos:checkout']);
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  togglePermission(code: string): void {
    this.selectedPermissionCodes.update(current =>
      current.includes(code)
        ? current.filter(c => c !== code)
        : [...current, code]
    );
  }

  isPermissionSelected(code: string): boolean {
    return this.selectedPermissionCodes().includes(code);
  }

  selectAllPermissions(): void {
    this.selectedPermissionCodes.set([...this.allCodes]);
  }

  clearAllPermissions(): void {
    this.selectedPermissionCodes.set([]);
  }

  async submitCreateRole(): Promise<void> {
    const name = this.newRoleName().trim();
    const codes = this.selectedPermissionCodes();

    if (!name) {
      this.toastService.error('Role name is required.');
      return;
    }
    if (codes.length === 0) {
      this.toastService.error('Select at least one permission.');
      return;
    }

    this.isCreating.set(true);
    try {
      const command: CreateRoleCommand = {
        name,
        permissionCodes: codes
      };
      await firstValueFrom(this.rolesClient.create(command));
      this.toastService.success(`Role '${name}' created successfully.`);
      this.closeCreateModal();
      await this.loadRoles();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create role.';
      this.toastService.error(msg);
    } finally {
      this.isCreating.set(false);
    }
  }

  private async parseFileResponse<T>(resp: FileResponse): Promise<T | null> {
    if (!resp || !resp.data) return null;
    if (typeof resp.data === 'string') {
      return JSON.parse(resp.data) as T;
    }
    if (resp.data instanceof Blob) {
      const text = await resp.data.text();
      return JSON.parse(text) as T;
    }
    return resp.data as T;
  }
}
