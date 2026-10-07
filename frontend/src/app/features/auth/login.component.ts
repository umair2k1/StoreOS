import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';

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
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);
  private readonly http = inject(HttpClient);
  private readonly authStore = inject(AuthSignalStore);
  private readonly router = inject(Router);
  readonly branding = inject(BrandingSignalStore);

  readonly form = this.fb.nonNullable.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    tenantId: ['']
  });

  readonly isSubmitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  submit(): void {
    if (this.form.invalid) {
      return;
    }

    this.isSubmitting.set(true);
    this.errorMessage.set(null);

    const val = this.form.getRawValue();
    const payload: { email: string; password: string; tenantId?: string } = {
      email: val.email,
      password: val.password
    };
    if (val.tenantId && val.tenantId.trim()) {
      payload.tenantId = val.tenantId.trim();
    }

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
        this.router.navigateByUrl('/pos');
      },
      error: (err: { error?: LoginErrorPayload }) => {
        this.isSubmitting.set(false);
        this.errorMessage.set(err?.error?.title || err?.error?.message || 'Login failed. Please check your credentials.');
      }
    });
  }
}
