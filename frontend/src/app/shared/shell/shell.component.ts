import { Component, effect, inject } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BranchSwitcherComponent } from './branch-switcher.component';
import { LockScreenComponent } from '../../features/auth/lock-screen.component';
import { LockScreenOverlayService } from '../../features/auth/lock-screen-overlay.service';
import { ToastContainerComponent } from '../components/toast-container/toast-container.component';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, BranchSwitcherComponent, LockScreenComponent, ToastContainerComponent],
  template: `
    <header class="shell-header">
      <span class="brand-name">{{ branding.tenantName() }}</span>
      @if (branding.activeBranchName(); as branchName) {
        <span class="branch-badge">{{ branchName }}</span>
      }
      <app-branch-switcher />
      <nav>
        <a routerLink="/pos">POS</a>
        <a routerLink="/sales">Sales</a>
        <a routerLink="/inventory">Inventory</a>
        <a routerLink="/purchases/import">Purchases</a>
        <a routerLink="/customers">Customers</a>
        <a routerLink="/suppliers">Suppliers</a>
        <a routerLink="/expenses">Expenses</a>
        <a routerLink="/liabilities">Liabilities</a>
        <a routerLink="/repairs">Repairs</a>
        <a routerLink="/reports">Reports</a>
        <a routerLink="/settings">Settings</a>
        <a routerLink="/admin/users">Admin</a>
      </nav>
    </header>
    <main><router-outlet /></main>
    @if (lockOverlay.isVisible()) {
      <app-lock-screen />
    }
    <app-toast-container />
  `,
  styleUrl: './shell.component.scss'
})
export class ShellComponent {
  branding = inject(BrandingSignalStore);
  private titleService = inject(Title);
  private auth = inject(AuthSignalStore);
  readonly lockOverlay = inject(LockScreenOverlayService);

  constructor() {
    effect(() => {
      const branchPart = this.branding.activeBranchName();
      const tenantPart = this.branding.tenantName();
      this.titleService.setTitle(branchPart ? `${branchPart} | ${tenantPart}` : tenantPart);
    });
  }
}
