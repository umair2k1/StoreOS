import { inject } from '@angular/core';
import { AuthRefreshService } from './auth-refresh.service';
import { firstValueFrom } from 'rxjs';

export function silentRefreshInitializer(): () => Promise<void> {
  const refreshService = inject(AuthRefreshService);
  return async () => {
    try {
      await firstValueFrom(refreshService.refresh());
    } catch {
      // No valid refresh cookie present (first visit, or fully logged out) - this is a normal,
      // expected outcome, not an error condition. The app proceeds unauthenticated; the auth
      // guard (P-1005) will redirect to /login when a protected route is visited.
    }
  };
}
