import { HttpErrorResponse } from '@angular/common/http';

/**
 * The shape of ASP.NET's built-in ProblemDetails / HttpValidationProblemDetails,
 * as actually returned by QuotesApi's Results.ValidationProblem(errors) and
 * Results.Json(new { error }, ...) calls. `errors` is only present on the
 * validation-failure form; `error` is the shape used by the auth endpoints.
 */
interface ProblemDetailsLike {
  readonly title?: string;
  readonly errors?: Record<string, string[]>;
  readonly error?: string;
}

/**
 * A normalized, typed error every HTTP call in this app resolves to, once it
 * passes through errorMappingInterceptor. Consuming code never touches a raw
 * HttpErrorResponse or has to know ASP.NET's ProblemDetails shape - it reads
 * `friendlyMessage` for display, or `fieldErrors` to target specific form
 * controls (the exact shape QuoteFormComponent's server-error mapping
 * already expected before this existed).
 */
export interface AppHttpError {
  readonly status: number;
  readonly friendlyMessage: string;
  readonly fieldErrors?: Record<string, string[]>;
  readonly raw: HttpErrorResponse;
}

export function toAppHttpError(error: HttpErrorResponse): AppHttpError {
  const body = error.error as ProblemDetailsLike | null;
  const fieldErrors = body?.errors;

  let friendlyMessage: string;
  if (fieldErrors && Object.keys(fieldErrors).length > 0) {
    friendlyMessage = Object.values(fieldErrors).flat().join(' ');
  } else if (body?.error) {
    friendlyMessage = body.error;
  } else if (error.status === 401) {
    friendlyMessage = 'You need to sign in to do that.';
  } else if (error.status === 403) {
    friendlyMessage = "You don't have permission to do that.";
  } else if (error.status === 404) {
    friendlyMessage = 'We could not find that.';
  } else if (error.status === 0) {
    friendlyMessage = 'Could not reach the server. Check your connection and try again.';
  } else if (body?.title) {
    friendlyMessage = body.title;
  } else {
    friendlyMessage = 'Something went wrong. Please try again.';
  }

  return { status: error.status, friendlyMessage, fieldErrors, raw: error };
}
