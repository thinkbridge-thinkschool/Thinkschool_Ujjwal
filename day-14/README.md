# Day 14 — Reactive forms + accessibility

Extends the existing Day 13 app (`day-13/quotes-ui`) rather than duplicating
it: a create-a-quote form was added alongside the quotes list and detail
components already there. No new Angular project was scaffolded.

## What was built

A `QuoteFormComponent` (reactive forms, `ReactiveFormsModule`) that POSTs to
the real Week-1 `QuotesApi` endpoint (`POST http://localhost:5296/api/quotes`).
The app has a small nav-driven shell (`App` in `app.ts`/`app.html`) with a
`view` signal switching between the quotes list and the create-quote form;
after a successful submit the view switches back to the list, which
re-mounts `QuotesComponent` and refetches, so the new quote shows up without
threading state between the two components.

Client-side validators mirror the server's `CreateQuoteRequest` contract:
required (matching the server's `string.IsNullOrWhiteSpace` check, not
Angular's built-in `Validators.required`, which does not treat whitespace-only
input as empty) and `maxLength` at 200/2000 characters, matching the DTO's
`[MaxLength]` annotations — even though those annotations are not currently
enforced server-side (verified live: the API accepted a 250-character author
with 201 Created). The client is intentionally stricter than the server here.

Full accessibility wiring: real `<label for>` on every input, `aria-invalid`
only once a control is both invalid and touched, `aria-describedby` pointing
at each field's error message (and absent entirely when there's no error),
error containers with `role="alert"`, focus moved programmatically to the
first invalid control on an invalid submit, and a `<fieldset>/<legend>`
grouping the two fields.

## Component path

`day-13/quotes-ui/src/app/components/quote-form/`
(`quote-form.ts`, `quote-form.html`, `quote-form.css`, `not-blank.validator.ts`)

## Branch

`day14-forms`

## Auth

`POST /api/quotes` requires a bearer token, so a login flow was added:
`AuthService` (`src/app/services/auth.ts`), `LoginComponent`
(`src/app/components/login/`, toggles between sign-in and create-account),
and `auth-interceptor.ts` attaches the stored token to requests against the
API origin. Logged-out users see only the login/create-account page; logged
-in users see a navbar (Quotes / Create quote / Log out) and the rest of
the app. `POST /api/auth/register` was added to `day-5/QuotesApi` (backend
change, requested directly rather than staying purely frontend) so sign-up
works with any email, not just the one seeded dev account
(`test@example.com` / `Password123!`, from the backend's pre-existing seed).

## UI

Quotes render as a card grid (not a plain list) - each card has a decorative
quote-mark, the text, and the author. Selecting a card shows its detail
below the grid. One shared accent color, 1px borders, no shadows, consistent
across the login page, navbar, cards, and the create-quote form.

## Known gap

The a11y wiring (labels, `aria-invalid`/`aria-describedby`, focus-to-first-
error) was verified by reading the rendered logic and Angular's own source
(e.g. confirming `Validators.required` doesn't trim whitespace), plus
curl-captured real server responses fed through the actual error-mapping
code. It was **not** verified with a live keyboard pass, screen reader, or
axe/Lighthouse audit - no browser was available in the environment this was
built in. That pass is still owed before calling this fully verified.
