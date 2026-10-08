import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  UsersClient,
  RolesClient,
  BranchesClient
} from '../../core/api/generated-client';
import type {
  CreateUserCommand,
  ResetPasswordBody,
  FileResponse
} from '../../core/api/generated-client';
import { ToastService } from '../../shared/services/toast.service';

export interface UserRoleAssignment {
  roleId: string;
  roleName: string;
  branchId?: string | null;
}

export interface UserBranchAssignment {
  branchId: string;
  branchName: string;
  branchCode: string;
  isDefault: boolean;
}

export interface UserItem {
  id: string;
  email: string;
  displayName: string;
  isActive: boolean;
  tokenVersion: number;
  roles?: UserRoleAssignment[];
  branches?: UserBranchAssignment[];
}

export interface RoleOption {
  id: string;
  name: string;
}

export interface BranchOption {
  id: string;
  name: string;
  code: string;
}

@Component({
  selector: 'app-users-admin',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './users-admin.component.html',
  styleUrl: './users-admin.component.scss'
})
export class UsersAdminComponent implements OnInit {
  private readonly usersClient = inject(UsersClient);
  private readonly rolesClient = inject(RolesClient);
  private readonly branchesClient = inject(BranchesClient);
  private readonly toastService = inject(ToastService);

  readonly users = signal<UserItem[]>([]);
  readonly roles = signal<RoleOption[]>([]);
  readonly branches = signal<BranchOption[]>([]);

  readonly isLoading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  // Create User Modal State
  readonly isCreateModalOpen = signal<boolean>(false);
  readonly isCreating = signal<boolean>(false);
  readonly newEmail = signal<string>('');
  readonly newDisplayName = signal<string>('');
  readonly newPassword = signal<string>('');
  readonly newRoleId = signal<string>('');
  readonly newBranchId = signal<string>('');

  // Reset Password Modal State (Review v2)
  readonly isResetModalOpen = signal<boolean>(false);
  readonly isResetting = signal<boolean>(false);
  readonly selectedUser = signal<UserItem | null>(null);
  readonly tempPassword = signal<string>('');

  ngOnInit(): void {
    this.initData();
  }

  async initData(): Promise<void> {
    this.isLoading.set(true);
    this.errorMessage.set(null);
    try {
      await Promise.all([
        this.loadUsers(),
        this.loadRoles(),
        this.loadBranches()
      ]);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to initialize users.';
      this.errorMessage.set(msg);
      this.toastService.error(msg);
    } finally {
      this.isLoading.set(false);
    }
  }

  async loadUsers(): Promise<void> {
    const resp = await firstValueFrom(this.usersClient.list());
    const parsed = await this.parseFileResponse<UserItem[]>(resp);
    this.users.set(Array.isArray(parsed) ? parsed : []);
  }

  async loadRoles(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.rolesClient.list());
      const parsed = await this.parseFileResponse<RoleOption[]>(resp);
      this.roles.set(Array.isArray(parsed) ? parsed : []);
    } catch {
      // Non-critical
    }
  }

  async loadBranches(): Promise<void> {
    try {
      const resp = await firstValueFrom(this.branchesClient.list());
      const parsed = await this.parseFileResponse<BranchOption[]>(resp);
      this.branches.set(Array.isArray(parsed) ? parsed : []);
    } catch {
      // Non-critical
    }
  }

  openCreateModal(): void {
    this.newEmail.set('');
    this.newDisplayName.set('');
    this.newPassword.set('');
    this.newRoleId.set(this.roles().length > 0 ? this.roles()[0].id : '');
    this.newBranchId.set(this.branches().length > 0 ? this.branches()[0].id : '');
    this.isCreateModalOpen.set(true);
  }

  closeCreateModal(): void {
    this.isCreateModalOpen.set(false);
  }

  async submitCreateUser(): Promise<void> {
    const email = this.newEmail().trim();
    const displayName = this.newDisplayName().trim();
    const password = this.newPassword();

    if (!email || !displayName || !password) {
      this.toastService.error('Email, display name, and password are required.');
      return;
    }

    this.isCreating.set(true);
    try {
      const command: CreateUserCommand = {
        email,
        displayName,
        password,
        initialRoleId: this.newRoleId() || undefined,
        initialBranchId: this.newBranchId() || undefined
      };

      await firstValueFrom(this.usersClient.create(command));
      this.toastService.success(`User '${displayName}' created.`);
      this.closeCreateModal();
      await this.loadUsers();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to create user.';
      this.toastService.error(msg);
    } finally {
      this.isCreating.set(false);
    }
  }

  async deactivateUser(user: UserItem): Promise<void> {
    try {
      await firstValueFrom(this.usersClient.deactivate(user.id));
      this.toastService.success(`User '${user.displayName}' deactivated.`);
      // Reflect new state in list
      this.users.update(list =>
        list.map(u => (u.id === user.id ? { ...u, isActive: false } : u))
      );
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to deactivate user.';
      this.toastService.error(msg);
    }
  }

  openResetPasswordModal(user: UserItem): void {
    this.selectedUser.set(user);
    this.tempPassword.set('');
    this.isResetModalOpen.set(true);
  }

  closeResetPasswordModal(): void {
    this.isResetModalOpen.set(false);
    this.selectedUser.set(null);
  }

  async submitResetPassword(): Promise<void> {
    const user = this.selectedUser();
    const tempPass = this.tempPassword();

    if (!user || !tempPass) {
      this.toastService.error('Temporary password is required.');
      return;
    }

    this.isResetting.set(true);
    try {
      const body: ResetPasswordBody = {
        newTemporaryPassword: tempPass
      };
      await firstValueFrom(this.usersClient.resetPassword(user.id, body));
      this.toastService.success(`Password reset for ${user.displayName}.`);
      this.closeResetPasswordModal();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to reset password.';
      this.toastService.error(msg);
    } finally {
      this.isResetting.set(false);
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
