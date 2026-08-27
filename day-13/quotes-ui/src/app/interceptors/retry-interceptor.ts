import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { retry, throwError, timer } from 'rxjs';

const MAX_RETRIES = 2;
const BASE_DELAY_MS = 300;

/**
 * Retries idempotent GET requests on transient failures (no response at all,
 * or a 5xx from the server) with exponential backoff: 300ms, then 600ms.
 * Never retries POST/DELETE (createQuote, deleteQuote) - those aren't
 * idempotent, and never retries a 4xx - a validation error or a 401 will
 * fail identically on every attempt, so retrying just delays the real error.
 */
export const retryInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'GET') {
    return next(req);
  }

  return next(req).pipe(
    retry({
      count: MAX_RETRIES,
      delay: (error: unknown, retryCount: number) => {
        const isTransient = error instanceof HttpErrorResponse && (error.status === 0 || error.status >= 500);
        if (!isTransient) {
          return throwError(() => error);
        }
        return timer(BASE_DELAY_MS * 2 ** (retryCount - 1));
      },
    }),
  );
};
