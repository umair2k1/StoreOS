import { Component, effect, inject } from '@angular/core';
import { RouterOutlet, RouterLink } from '@angular/router';
import { Title } from '@angular/platform-browser';
import { BrandingSignalStore } from '../../core/branding/branding-signal-store';
import { AuthSignalStore } from '../../core/auth/auth-signal-store';
import { BranchSwitcherComponent } from './branch-switcher.component';
import { LockScreenComponent } from '../../features/auth/lock-screen.component';
import { LockScreenOverlayService } from '../../features/auth/lock-screen-overlay.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, BranchSwitcherComponent, LockScreenComponent],
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
        <a routerLink="/customers">Customers</a>
        <a routerLink="/reports">Reports</a>
      </nav>
    </header>
    <main><router-outlet /></main>
    @if (lockOverlay.isVisible()) {
      <app-lock-screen />
    }
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
