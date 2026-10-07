import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { Subject, throwError } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { AuthSignalStore } from '../auth/auth-signal-store';
import { AuthRefreshService } from '../auth/auth-refresh.service';

let isRefreshing = false;
let refreshSubject: Subject<void> | null = null;

export function resetAuthInterceptorState(): void {
  isRefreshing = false;
  refreshSubject = null;
}

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authStore = inject(AuthSignalStore);
  const refreshService = inject(AuthRefreshService);

  const token = authStore.accessToken();
  const authedReq = token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(authedReq).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || req.url.includes('/auth/refresh') || req.url.includes('/auth/login')) {
        return throwError(() => err);
      }

      const currentToken = authStore.accessToken();
      if (token && currentToken && currentToken !== token) {
        const retried = req.clone({ setHeaders: { Authorization: `Bearer ${currentToken}` } });
        return next(retried);
      }

      if (!isRefreshing) {
        isRefreshing = true;
        refreshSubject = new Subject<void>();

        return refreshService.refresh().pipe(
          switchMap(() => {
            isRefreshing = false;
            const subject = refreshSubject;
            refreshSubject = null;
            subject?.next();
            subject?.complete();

            const newToken = authStore.accessToken();
            const retried = newToken
              ? req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } })
              : req;
            return next(retried);
          }),
          catchError((refreshErr) => {
            isRefreshing = false;
            const subject = refreshSubject;
            refreshSubject = null;
            authStore.clear();
            subject?.error(refreshErr);
            return throwError(() => refreshErr);
          })
        );
      } else {
        return refreshSubject!.pipe(
          switchMap(() => {
            const newToken = authStore.accessToken();
            const retried = newToken
              ? req.clone({ setHeaders: { Authorization: `Bearer ${newToken}` } })
              : req;
            return next(retried);
          })
        );
      }
    })
  );
};
