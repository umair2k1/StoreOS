import { ApplicationConfig, provideBrowserGlobalErrorListeners, isDevMode, provideAppInitializer } from '@angular/core';
import { provideRouter } from '@angular/router';
import { withInterceptors } from '@angular/common/http';
import { provideServiceWorker } from '@angular/service-worker';
import { provideApiConfig } from './core/api/api-config';
import { authInterceptor } from './core/interceptors/auth.interceptor';
import { silentRefreshInitializer } from './core/auth/silent-refresh';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideApiConfig(withInterceptors([authInterceptor])),
    provideAppInitializer(() => silentRefreshInitializer()()),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
