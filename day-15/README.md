# Day 15 — Task 1: HttpClient + interceptors, directed via Claude Code

All code for this task lives in [`day-13/quotes-ui`](../day-13/quotes-ui) (the
existing Angular app), not in this folder — this README documents what the
agent was asked to do, what it produced, and how it was verified. Branch:
`day15-http-interceptors`.

## 1. The brief given to the agent

> Same goal, but you direct an agent (Claude Code) instead of hand-writing
> it — directing + verifying AI is the actual job. First, have it write a
> characterization test that pins your real Week-1 API contract — a real
> endpoint like `GET /api/quotes?page=N&size=N`, its actual shape (`{id,
> author, text}`), and a real 4xx coming back as
> `ProblemDetails`/`ValidationProblemDetails` — green BEFORE any UI. Then
> direct it to wire HttpClient + functional interceptors against that
> contract: auth header, retry idempotent GETs with backoff, map
> ProblemDetails to a typed app error that surfaces a friendly message on a
> 4xx. You own the brief and you own the verification — read the diff like
> a junior's PR and show your work. Be ready to defend any line the agent
> wrote.

Concretely, I directed the agent to:

1. Read the real backend contract from source (`day-5/QuotesApi`), not
   assume it.
2. Write a characterization test against the *unmodified* `QuoteService` and
   get it green before touching any UI.
3. Add a retry interceptor: idempotent GETs only, exponential backoff, never
   retry a 4xx.
4. Add a typed `AppHttpError` + mapping interceptor so ProblemDetails/4xx
   responses surface a friendly message instead of a raw
   `HttpErrorResponse`.
5. Wire everything into `app.config.ts` in the correct order and migrate the
   components that currently hand-roll error parsing.
6. Verify live in a browser against the real backend, not just unit tests.

## 2. The real Week-1 contract this pins against

Read directly from
[`day-5/QuotesApi/Extensions/EndpointExtensions.cs`](../day-5/QuotesApi/Extensions/EndpointExtensions.cs):

| Endpoint | Behavior |
|---|---|
| `GET /api/quotes?page=N&size=N` | 200, `Quote[]` shaped `{id: number, author: string, text: string, createdByUserId: string \| null}` |
| `GET /api/quotes/{id}` | 200 `Quote`, or 404 |
| `POST /api/quotes` (invalid body) | 400 `HttpValidationProblemDetails`: `{type, title, status, errors: Record<string,string[]>}` |
| `POST /api/auth/login` (bad credentials) | 401, **empty body** (`Results.Unauthorized()`) |
| `POST /api/auth/register` (duplicate email) | 409, `{error: string}` (`Results.Conflict(new { error = "..." })`) |
| `DELETE /api/quotes/{id}` (not the owner) | 403 |

The 401-with-empty-body and the two different error-body shapes
(`{errors: {...}}` vs `{error: "..."}`) both turned out to matter for the
error-mapping interceptor — see §5.

## 3. What the agent produced

### Characterization test — green before any UI change
[`src/app/services/quote.spec.ts`](../day-13/quotes-ui/src/app/services/quote.spec.ts)
— 5 tests against the **unmodified** `QuoteService`, using
`HttpTestingController`:
- `GET /api/quotes?page=1&size=100` returns `Quote[]` shaped correctly (field
  types asserted individually, not just object equality against a fixture).
- `GET /api/quotes/{id}` hits the real per-id route.
- `POST /api/quotes` never sends server-owned fields (`id`,
  `createdByUserId`).
- A 400 from `POST /api/quotes` surfaces the real
  `HttpValidationProblemDetails` shape untouched (payload captured verbatim
  from an earlier live run).
- `DELETE /api/quotes/{id}` hits the real route with no body.

This required setting up a test runner from scratch — `ng test` was not
wired up in this project at all (`angular.json` had no `test` architect
target). Added the `@angular/build:unit-test` builder with the `vitest`
runner, and installed `vitest`/`jsdom` (optional peer deps of
`@angular/build`, not bundled).

### Typed error model
[`src/app/http/app-http-error.ts`](../day-13/quotes-ui/src/app/http/app-http-error.ts)
— `AppHttpError { status, friendlyMessage, fieldErrors?, raw }` and
`toAppHttpError(HttpErrorResponse): AppHttpError`, which inspects the real
response shapes this backend sends (validation `errors`, auth `{error}`,
or nothing) and falls back through: field errors → `body.error` →
status-specific copy (401/403/404/0) → `body.title` → generic fallback.

Covered by
[`src/app/http/app-http-error.spec.ts`](../day-13/quotes-ui/src/app/http/app-http-error.spec.ts)
(7 tests) — this file did not exist until I noticed the mapping function had
zero test coverage (the characterization test only covers `QuoteService`,
not the interceptor pipeline). Each case is pinned against a real response
shape/string read from `EndpointExtensions.cs`, not an invented message.

### Interceptors
- [`src/app/interceptors/auth-interceptor.ts`](../day-13/quotes-ui/src/app/interceptors/auth-interceptor.ts)
  — pre-existing, attaches the Bearer token; left as-is, it already met the
  brief.
- [`src/app/interceptors/retry-interceptor.ts`](../day-13/quotes-ui/src/app/interceptors/retry-interceptor.ts)
  (new) — GET-only, retries up to twice with exponential backoff
  (300ms, 600ms), and only when the error is transient (`status === 0` or
  `status >= 500`); any 4xx passes straight through.
- [`src/app/interceptors/error-mapping-interceptor.ts`](../day-13/quotes-ui/src/app/interceptors/error-mapping-interceptor.ts)
  (new) — catches the final `HttpErrorResponse` and rethrows it as
  `AppHttpError` via `toAppHttpError`.

### Wiring order
[`src/app/app.config.ts`](../day-13/quotes-ui/src/app/app.config.ts):
```ts
provideHttpClient(withInterceptors([authInterceptor, errorMappingInterceptor, retryInterceptor]))
```
Angular runs the request through the array in order and the response/error
back through it in reverse, so the **last** interceptor sits closest to the
backend. `retryInterceptor` has to be last so it retries the actual HTTP
call; `errorMappingInterceptor` sits just before it so it only maps the
*final* error once retries are exhausted, not every transient attempt in
between.

### Component migration
Four components previously parsed `HttpErrorResponse` (and ASP.NET's
`ValidationProblemBody` shape) by hand. All four now consume `AppHttpError`
instead:
- [`login.ts`](../day-13/quotes-ui/src/app/components/login/login.ts) — kept
  one deliberate override: the interceptor's generic 401 message ("You need
  to sign in to do that.") is wrong on the login form itself, since the
  backend sends no body on bad credentials and a 401 *there* always means
  bad credentials, not "not authenticated for a resource." Login overrides
  that one case with "Invalid email or password."
- [`quote-form.ts`](../day-13/quotes-ui/src/app/components/quote-form/quote-form.ts)
  — now reads `err.fieldErrors` / `err.friendlyMessage` directly instead of
  parsing the ProblemDetails body itself.
- [`quote-form-signal.ts`](../day-13/quotes-ui/src/app/components/quote-form-signal/quote-form-signal.ts)
  — same simplification for the Signal Forms version.
- [`quote-detail.ts`](../day-13/quotes-ui/src/app/components/quote-detail/quote-detail.ts)
  — delete handler keeps one override (403 → "You can only delete quotes
  you created.", more precise than the generic permission message), else
  uses `err.friendlyMessage`.

`ValidationProblemBody` (the old hand-rolled ProblemDetails type in
`quote.model.ts`) was deleted — nothing references it any more.

## 4. Reading the diff like a junior's PR — what I pushed back on / would defend

- **The interceptor order isn't obvious and the agent got it right, but I
  made it verify it, not just assert it** — see §5's retry-count tests. An
  interceptor order bug (e.g. error-mapping before retry) would still build
  and pass the characterization test, since that test never touches the
  interceptor pipeline. Only the live retry-count check actually proves the
  order is correct.
- **The two login-specific and delete-specific message overrides are
  deliberate, not leftover duplication.** I checked both against the real
  backend behavior (empty 401 body on login; 403 meaning ownership mismatch
  on delete) before accepting them — a fully generic interceptor message
  would have been either wrong (login) or vaguer than necessary (delete).
- **I would not have accepted the interceptor code as-is if `toAppHttpError`
  had shipped without its own test.** The characterization test's green
  status only proves `QuoteService` is unaffected — it says nothing about
  whether the new mapping logic is correct. That gap is why
  `app-http-error.spec.ts` exists.

## 5. Verification log

Backend running at `http://localhost:5296` (already up, confirmed
`GET /api/quotes` → 200 before starting). Frontend served via `ng serve` at
`http://localhost:4200`. Driven with Playwright (`playwright-core`) against
the system-installed Chrome — real browser, real backend, no mocks.

### Automated (`ng test`, vitest)
```
Test Files  2 passed (2)
     Tests  12 passed (12)
```
5 characterization tests (`quote.spec.ts`) + 7 error-mapping tests
(`app-http-error.spec.ts`). Ran green after every interceptor/component
change, per the brief's "green before any UI" sequencing.

### Live, states/edges exercised
| # | What was done | Result |
|---|---|---|
| 1 | Load app with no session | App is fully login-gated (`app.html` — no anonymous quotes view exists at all; this corrected my own initial assumption, not a bug). Login form renders. |
| 2 | Log in with `test@example.com` / wrong password | Real `401` from `/api/auth/login` with an **empty body** → `AppHttpError` → login's override → **"Invalid email or password."** shown |
| 3 | Log in with correct password | Lands on Quotes view (app default), "Create quote" nav reveals the add-quote form |
| 4 | Submit Add Quote with author left blank | Client-side `notBlank` validator blocks it before any HTTP call — "Author is required." (confirms client validation guards the request; server's 400 path is covered by the unit tests in §3, not re-triggered live since nothing currently produces a client-vs-server validation mismatch) |
| 5 | Submit Add Quote with valid author/text | 201, quote appears in the list with no page reload; "Quote added." success message **visibly renders** (see bug below) |
| 6 | Force `503` on every `GET /api/quotes` via route interception, then trigger a refetch | **3 attempts observed** (1 original + 2 retries) — confirms `retryInterceptor`'s exponential backoff actually retries a transient failure |
| 7 | Force `404` on every `GET /api/quotes`, then trigger a refetch | **1 attempt observed, no retries** — confirms 4xx is never retried |
| 8 | Force `connectionrefused` on `POST /api/quotes` | After retry attempts inside the pipeline resolve, friendly message shown: **"Could not reach the server. Check your connection and try again."** (~2.5s elapsed, consistent with the 300ms/600ms backoff plus request round-trips) |
| 9 | Console errors during the whole run | Exactly two: the deliberate `401` from step 2 and the deliberate `net::ERR_CONNECTION_REFUSED` from step 8 — both expected, nothing else |

Test quotes created during step 5/6 verification runs (`Day15 Verification
Author`, ids 39–42) were deleted via `DELETE /api/quotes/{id}` after
verification so the shared dev database wasn't left with junk data.

### One concrete bug caught and fixed
**The "Quote added." success message was dead code.** In
`QuoteFormComponent.onSubmit()`, the `next` handler does:
```ts
this.state.set('success');
this.quoteCreated.emit(quote);
```
`quoteCreated` is handled synchronously by `App.onQuoteCreated()`, which
immediately called `this.view.set('quotes')` — switching the `@switch` in
`app.html` away from `<app-quote-form>` in the same tick that set its state
to `'success'`. The success paragraph was written but never painted; a
Playwright check confirmed `successMessage` came back `null` even though the
quote really was created (id 39, verified via the API directly). Fixed by
delaying the view switch 900ms in `app.ts` so the confirmation is visible
before navigating back to the list — re-verified live afterward
(`successMessageVisibleBeforeNav: "Quote added."`).

This was caught by live interaction, not by reading the code or by the unit
tests — none of the 12 automated tests touch `App`'s view-switching at all.

### What breaks if the API contract changes
- **Field renamed/removed on `Quote`** (e.g. `text` → `body`): the
  characterization test's per-field type assertions in `quote.spec.ts` fail
  immediately — that's their whole purpose. The app itself would silently
  show `undefined` in the template rather than erroring, since nothing
  currently validates the response shape at runtime.
- **Validation error shape changes** (e.g. ASP.NET's `errors` key renamed,
  or a plain array instead of `Record<string,string[]>`): `toAppHttpError`
  would fail to find `fieldErrors`, silently fall through to the generic
  fallback message, and `QuoteFormComponent`/`QuoteFormSignalComponent`
  would stop highlighting the specific invalid field — a real regression
  that would currently ship without any test failing, since
  `app-http-error.spec.ts` only pins the *current* shape, not a schema
  guarantee.
- **Login starts returning a body on 401** (e.g. `{error: "Invalid
  credentials"}` instead of empty): harmless — `LoginComponent`'s override
  ignores `friendlyMessage` entirely for status 401 and always shows its own
  copy, so this wouldn't change behavior either way. Worth noting as the one
  place a contract change wouldn't be caught by any test *or* change visible
  behavior, for better or worse.
- **A previously-4xx failure mode moves to 5xx** (or vice versa): changes
  retry behavior silently — a request that used to fail once now retries
  twice before failing, or a request that used to retry now fails
  immediately. Nothing currently tests this boundary against the real
  backend's actual status codes, only against synthetic 503/404 in the live
  driver run above.

## 6. Summary

Directed Claude Code to build HttpClient interceptor infrastructure (auth,
retry-with-backoff, typed error mapping) against the real Week-1
`QuotesApi` contract, starting from a characterization test that pinned that
contract before any UI code changed. Reviewed and verified rather than
trusted: added a missing unit test for the error-mapping logic that the
agent's own characterization test didn't cover, checked the interceptor
ordering live rather than by inspection (3 attempts on a forced 503, 1 on a
forced 404), and caught a genuine UI bug — a success message that could
never actually render — via live browser interaction that none of the
automated tests would have surfaced. Test data created during verification
was cleaned up from the shared dev database afterward.
