import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';
import { LockScreenOverlayService } from './lock-screen-overlay.service';

interface LoginResponse {
  accessToken: string;
  expiresAt: string;
  userId: string;
  displayName: string;
  tenantId: string;
  allBranches: boolean;
  branchIds: string[];
  permissions: string[];
}

interface LoginErrorPayload {
  title?: string;
  message?: string;
}

@Component({
  selector: 'app-lock-screen',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './lock-screen.component.html',
  styleUrl: './lock-screen.component.scss'
})
export class LockScreenComponent {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  readonly authStore = inject(AuthSignalStore);
  readonly branding = inject(BrandingSignalStore);
  readonly lockOverlay = inject(LockScreenOverlayService);

  readonly form = this.fb.nonNullable.group({
    password: ['', Validators.required]
  });

  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const email = this.authStore.displayName() || '';
    const tenantId = this.authStore.tenantId() || '';
    const payload = {
      email,
      password: this.form.getRawValue().password,
      tenantId: tenantId || undefined
    };

    this.http.post<LoginResponse>('/api/auth/login', payload, { withCredentials: true }).subscribe({
      next: (response) => {
        this.authStore.setAuthenticated({
          accessToken: response.accessToken,
          expiresAt: response.expiresAt,
          userId: response.userId,
          displayName: response.displayName,
          tenantId: response.tenantId,
          allBranches: response.allBranches,
          branchIds: response.branchIds || [],
          permissions: response.permissions || []
        });
        this.isSubmitting.set(false);
        this.lockOverlay.hide();
      },
      error: (err: { error?: LoginErrorPayload }) => {
        this.isSubmitting.set(false);
        this.errorMessage.set(err?.error?.title || err?.error?.message || 'Incorrect password. Please try again.');
      }
    });
  }

  logout(): void {
    this.authStore.clear();
    this.lockOverlay.hide();
    this.router.navigateByUrl('/login');
  }
}
