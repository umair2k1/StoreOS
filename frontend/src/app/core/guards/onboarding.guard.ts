import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { map, catchError, of } from 'rxjs';
import { AuthSignalStore } from '../auth/auth-signal-store';

export interface BranchDto {
  id: string;
  code: string;
  name: string;
}

export const onboardingGuard: CanActivateFn = () => {
  const authStore = inject(AuthSignalStore);
  const router = inject(Router);
  const http = inject(HttpClient);

  if (!authStore.isAuthenticated()) {
    return router.createUrlTree(['/login']);
  }

  return http.get<BranchDto[]>('/api/branches').pipe(
    map(branches => {
      if (!branches || branches.length === 0) {
        if (authStore.hasPermission('branches:manage')) {
          return router.createUrlTree(['/onboarding']);
        }
        return router.createUrlTree(['/awaiting-setup']);
      }
      return true;
    }),
    catchError(() => of(true))
  );
};
