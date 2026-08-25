# Day 14 — Reactive forms + accessibility, and a Signal Forms rebuild

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

## Piece 2 — the same form, rebuilt with Signal Forms preview

`QuoteFormSignalComponent`
(`day-13/quotes-ui/src/app/components/quote-form-signal/`), on branch
`day14-signal-forms`. Same real contract, same fields, same
`POST /api/quotes` - rebuilt against `@angular/forms/signals`
(`form()`, `required()`, `maxLength()`, `validate()`, `submit()`, the
`[formField]` directive) instead of `ReactiveFormsModule`. Wired into the
nav as a third view ("Create quote (Signal Forms)") alongside the reactive
version rather than replacing it, so both are directly comparable.

Before writing any of it, the actual preview API was read from
`node_modules/@angular/forms/types/signals.d.ts` and the compiled
`fesm2022/signals.mjs` - not assumed from the doc comments, which turned out
to name the wrong directive (`[control]` in an example vs. the real
`[formField]`/`FormField`).

### Two things checked and caught before shipping, not guessed

- **`required()` has the same whitespace gap as `Validators.required`.**
  Read the compiled source directly: `isEmpty(value)` is
  `value === '' || value === false || value == null` - a whitespace-only
  string doesn't match, so it passes. Same fix as the reactive version: a
  supplementary `validate()` that explicitly trims. This is not a hypothetical
  - curl against the live API confirms the server rejects `"   "` as author
  with the same `400` either form would need to handle.
- **No automatic ARIA wiring.** It would have been reasonable to assume
  otherwise - `required()`/`maxLength()` *do* automatically set the real
  native `required`/`maxlength` HTML attributes (confirmed in
  `setNativeDomProperty` in the compiled bundle: `disabled`, `readonly`,
  `required`, `max`, `min`, `minLength`, `maxLength` are all wired straight
  onto the DOM element). `aria-invalid` and `aria-describedby` are not in
  that list. Checked before writing the template, so the form still hand-wires
  both exactly as the reactive version does - full a11y is not free here.

### Where it's actually simpler than the reactive version

- `maxLength()` sets the real native `maxlength` attribute, so the browser
  itself blocks typing past 200/2000 characters - the reactive version's
  `Validators.maxLength` never touches the DOM, so nothing stops the browser
  from accepting more.
- `submit()` calls `markAllAsTouched()` internally before checking validity,
  so a failed submit surfaces every error without a manual
  `form.markAllAsTouched()` call.
- `submit()` tracks its own `submitting()` signal per field - no `state`
  signal to hand-manage.
- Mapping server field errors is a plain array of
  `{fieldTree, kind, message}` objects returned from the submit action -
  no `control.setErrors({...control.errors, server: ...})` merge dance.

### Where it's still rough

- No automatic a11y wiring (above) - it looks like it should be closer to
  parity with a component-based UI library than it is.
- The whitespace gap in `required()` (above) is the same trap as reactive
  forms', just less expected in a newer API.
- Far less community precedent - most patterns here came from reading
  `.d.ts`/`.mjs` source directly rather than established docs or Stack
  Overflow answers, which is a real cost for anyone maintaining this later.

### Verified

- Empty/whitespace submit: both fields' `validate()` trims and rejects,
  matching the server exactly (confirmed via curl in this session).
- `touched()`/`dirty()` are rendered live in the template
  (`touched: {{ … }} · dirty: {{ … }}` under each field) rather than only
  asserted - visible signal state, not a claim about state.
- Clean submit: `POST /api/quotes` with `{author, text}` only (the shape
  `field().value()` actually produces) returned `201` with the full `Quote`
  in this session's verification.
- Failed submit (400): curl-captured real `ValidationProblem` body fed
  through the same field-targeting logic the component uses, confirming
  `author`/`text` map to `quoteForm.author`/`quoteForm.text` and not the
  reverse.
- **Not verified**: a live keyboard pass or axe/Lighthouse audit - same
  environment limitation as Piece 1, not glossed over here either.
