import { EnvironmentProviders, makeEnvironmentProviders } from '@angular/core';
import { HttpFeature, HttpFeatureKind, provideHttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environment';
import { API_BASE_URL } from './generated-client';

export { API_BASE_URL } from './generated-client';

export function provideApiConfig(...features: HttpFeature<HttpFeatureKind>[]): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideHttpClient(...features),
    {
      provide: API_BASE_URL,
      useFactory: () => {
        const trimmed = environment.apiBaseUrl.trim().replace(/\/+$/, '');
        return trimmed.endsWith('/api') ? trimmed.slice(0, -4) : trimmed;
      }
    }
  ]);
}
