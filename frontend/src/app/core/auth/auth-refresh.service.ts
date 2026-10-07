import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, throwError } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';
import { AuthSignalStore } from './auth-signal-store';

export interface RefreshResultPayload {
  accessToken: string;
  expiresAt: string;
  userId: string;
  displayName: string;
  tenantId: string;
  allBranches: boolean;
  branchIds: string[];
  permissions: string[];
}

@Injectable({ providedIn: 'root' })
export class AuthRefreshService {
  constructor(
    private readonly http: HttpClient,
    private readonly authStore: AuthSignalStore
  ) {}

  refresh(): Observable<void> {
    return this.http
      .post<RefreshResultPayload>('/api/auth/refresh', {}, { withCredentials: true })
      .pipe(
        tap((response) =>
          this.authStore.setAuthenticated({
            accessToken: response.accessToken,
            expiresAt: response.expiresAt,
            userId: response.userId,
            displayName: response.displayName,
            tenantId: response.tenantId,
            allBranches: response.allBranches,
            branchIds: response.branchIds ?? [],
            permissions: response.permissions ?? []
          })
        ),
        map(() => void 0),
        catchError((err) => {
          this.authStore.clear();
          return throwError(() => err);
        })
      );
  }
}
