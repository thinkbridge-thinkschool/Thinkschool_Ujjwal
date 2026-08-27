import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';
import { toAppHttpError } from '../http/app-http-error';

/**
 * Runs after retryInterceptor, so it only sees the FINAL error - the raw
 * HttpErrorResponse once retries (if any) are exhausted. Converts it to the
 * typed AppHttpError every component in this app should catch instead of a
 * raw HttpErrorResponse, so no component needs to know ASP.NET's
 * ProblemDetails/ValidationProblemDetails shape.
 */
export const errorMappingInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }
      return throwError(() => toAppHttpError(error));
    }),
  );
};
