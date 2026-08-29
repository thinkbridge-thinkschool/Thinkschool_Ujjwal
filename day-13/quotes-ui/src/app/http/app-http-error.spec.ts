import { HttpErrorResponse } from '@angular/common/http';
import { toAppHttpError } from './app-http-error';
import { environment } from '../../environments/environment';

function httpError(status: number, body: unknown, statusText = ''): HttpErrorResponse {
  return new HttpErrorResponse({ status, statusText, error: body, url: `${environment.apiOrigin}/api/quotes` });
}

// toAppHttpError is the piece errorMappingInterceptor delegates to. Pinned
// here against the real response shapes this backend actually sends
// (verified live and via EndpointExtensions.cs), same sourcing discipline
// as quote.spec.ts's characterization test.
describe('toAppHttpError - maps real backend error shapes to a friendly message', () => {
  it('a 400 HttpValidationProblemDetails from POST /api/quotes joins field errors into friendlyMessage', () => {
    const err = httpError(400, {
      type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
      title: 'One or more validation errors occurred.',
      status: 400,
      errors: { author: ['Author is required.'] },
    });

    const mapped = toAppHttpError(err);

    expect(mapped.status).toBe(400);
    expect(mapped.fieldErrors).toEqual({ author: ['Author is required.'] });
    expect(mapped.friendlyMessage).toBe('Author is required.');
  });

  it('a 401 from POST /api/auth/login (Results.Unauthorized(), empty body) falls back to a generic message', () => {
    // The real login endpoint sends no body on bad credentials - confirmed
    // in EndpointExtensions.cs (`return Results.Unauthorized();`). LoginComponent
    // overrides this specific case with copy suited to the login form; this
    // test pins what every OTHER 401 consumer sees by default.
    const err = httpError(401, null);

    const mapped = toAppHttpError(err);

    expect(mapped.status).toBe(401);
    expect(mapped.friendlyMessage).toBe('You need to sign in to do that.');
  });

  it('a 403 from DELETE /api/quotes/{id} (not the owner) maps to a permission message', () => {
    const err = httpError(403, null);

    const mapped = toAppHttpError(err);

    expect(mapped.friendlyMessage).toBe("You don't have permission to do that.");
  });

  it('a 404 from GET /api/quotes/{id} maps to a not-found message', () => {
    const err = httpError(404, null);

    expect(toAppHttpError(err).friendlyMessage).toBe('We could not find that.');
  });

  it('status 0 (no response - offline, CORS failure, connection refused) maps to a connectivity message', () => {
    const err = httpError(0, null, 'Unknown Error');

    expect(toAppHttpError(err).friendlyMessage).toBe('Could not reach the server. Check your connection and try again.');
  });

  it('the {error: string} shape used by auth endpoints surfaces verbatim - real 409 body from POST /api/auth/register on a duplicate email', () => {
    // Exact string from EndpointExtensions.cs:
    // Results.Conflict(new { error = "An account with this email already exists." })
    const err = httpError(409, { error: 'An account with this email already exists.' });

    expect(toAppHttpError(err).friendlyMessage).toBe('An account with this email already exists.');
  });

  it('always carries the raw HttpErrorResponse through, for callers that need more than the friendly message', () => {
    const err = httpError(500, null);

    expect(toAppHttpError(err).raw).toBe(err);
  });
});
