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
      {
        path: 'pos',
        children: [
          { path: '', loadComponent: () => import('./features/pos/pos.component').then(m => m.PosComponent) },
          { path: 'estimates/:id', loadComponent: () => import('./features/pos/estimate/estimate-detail.component').then(m => m.EstimateDetailComponent) }
        ]
      },
      {
        path: 'sales',
        children: [
          { path: '', loadComponent: () => import('./features/sales-history/sales-history.component').then(m => m.SalesHistoryComponent) },
          { path: 'estimates/:id', loadComponent: () => import('./features/pos/estimate/estimate-detail.component').then(m => m.EstimateDetailComponent) },
          { path: ':id', loadComponent: () => import('./features/sales-history/detail/sale-detail.component').then(m => m.SaleDetailComponent) }
        ]
      },
      {
        path: 'inventory',
        children: [
          { path: '', loadComponent: () => import('./features/inventory/inventory-list.component').then(m => m.InventoryListComponent) },
          { path: ':id', loadComponent: () => import('./features/inventory/detail/inventory-detail.component').then(m => m.InventoryDetailComponent) }
        ]
      },
      {
        path: 'purchases',
        children: [
          { path: 'import', loadComponent: () => import('./features/purchases/import/supplier-invoice-import.component').then(m => m.SupplierInvoiceImportComponent) }
        ]
      },
      {
        path: 'transfers',
        children: [
          { path: '', redirectTo: 'receive', pathMatch: 'full' },
          { path: 'out', loadComponent: () => import('./features/transfers/transfer-out.component').then(m => m.TransferOutComponent) },
          { path: 'receive', loadComponent: () => import('./features/transfers/receive-transfer.component').then(m => m.ReceiveTransferComponent) }
        ]
      },
      {
        path: 'customers',
        children: [
          { path: '', loadComponent: () => import('./features/customers/customers-list.component').then(m => m.CustomersListComponent) },
          { path: ':id', loadComponent: () => import('./features/customers/detail/customer-detail.component').then(m => m.CustomerDetailComponent) }
        ]
      },
      {
        path: 'suppliers',
        children: [
          { path: '', loadComponent: () => import('./features/suppliers/suppliers-list.component').then(m => m.SuppliersListComponent) },
          { path: ':id/returns/new', loadComponent: () => import('./features/suppliers/returns/create-return.component').then(m => m.CreateReturnComponent) },
          { path: ':id', loadComponent: () => import('./features/suppliers/detail/supplier-detail.component').then(m => m.SupplierDetailComponent) }
        ]
      },
      {
        path: 'expenses',
        loadComponent: () => import('./features/expenses/expenses.component').then(m => m.ExpensesComponent)
      },
      {
        path: 'liabilities',
        loadComponent: () => import('./features/liabilities/liabilities.component').then(m => m.LiabilitiesComponent)
      },
      { path: 'reports', children: [] },
    ]
  }
];
