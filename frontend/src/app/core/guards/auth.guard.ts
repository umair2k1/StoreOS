import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthSignalStore } from '../auth/auth-signal-store';

export const authGuard: CanActivateFn = () => {
  const authStore = inject(AuthSignalStore);
  const router = inject(Router);
  if (authStore.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};
