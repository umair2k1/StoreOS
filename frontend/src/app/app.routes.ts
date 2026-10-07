import { Routes } from '@angular/router';
import { ShellComponent } from './shared/shell/shell.component';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
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
