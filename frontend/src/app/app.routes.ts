import { Routes } from '@angular/router';
import { ShellComponent } from './shared/shell/shell.component';
import { authGuard } from './core/guards/auth.guard';
import { onboardingGuard } from './core/guards/onboarding.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'onboarding',
    loadComponent: () => import('./features/onboarding/onboarding-wizard.component').then(m => m.OnboardingWizardComponent),
    canActivate: [authGuard]
  },
  {
    path: 'awaiting-setup',
    loadComponent: () => import('./features/onboarding/awaiting-setup.component').then(m => m.AwaitingSetupComponent),
    canActivate: [authGuard]
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard, onboardingGuard],
    children: [
      { path: '', redirectTo: 'pos', pathMatch: 'full' },
      { path: 'pos', children: [] },
      { path: 'sales', children: [] },
      { path: 'inventory', children: [] },
      { path: 'customers', children: [] },
      { path: 'reports', children: [] },
    ]
  }
];
