import { ApplicationConfig, provideBrowserGlobalErrorListeners, provideZonelessChangeDetection } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { authInterceptor } from './interceptors/auth-interceptor';
import { errorMappingInterceptor } from './interceptors/error-mapping-interceptor';
import { retryInterceptor } from './interceptors/retry-interceptor';

// Order matters: the LAST interceptor in this array sits closest to the
// backend (Angular runs requests a->b->c and responses/errors c->b->a), so
// retryInterceptor goes last to retry the actual HTTP call, and
// errorMappingInterceptor sits just before it so it only maps the FINAL
// error once retries are exhausted, not each transient attempt.
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor])),
  ]
};
