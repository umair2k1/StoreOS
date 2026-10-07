import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';

@Component({
  selector: 'app-awaiting-setup',
  standalone: true,
  template: `
    <div class="awaiting-setup-container">
      <h1>Tenant Setup in Progress</h1>
      <p>
        Your shop has not been configured with an active branch yet.
        Please contact your store administrator to complete onboarding.
      </p>
      <button type="button" (click)="onLogout()">Log Out</button>
    </div>
  `,
  styleUrl: './awaiting-setup.component.scss'
})
export class AwaitingSetupComponent {
  private readonly authStore = inject(AuthSignalStore);
  private readonly router = inject(Router);

  onLogout(): void {
    this.authStore.clear();
    this.router.navigateByUrl('/login');
  }
}
