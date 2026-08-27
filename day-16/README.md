# Day 16 — Convert manual view-switching to Angular routing

All code for this task lives in [`day-13/quotes-ui`](../day-13/quotes-ui) —
this extends the existing app rather than duplicating it, same pattern as
Day 14 and Day 15. This folder holds only the write-up. Branch:
`day16-routing`.

## What changed

The app used to hold one `view` signal in `App` and switch between
`'quotes' | 'create' | 'create-signal'` with an `@switch` in `app.html` —
selection state for the detail panel lived in `QuotesComponent` as another
signal, with `QuoteDetailComponent` embedded as a child. None of that was
addressable by URL: no back button, no direct link to a quote, no bookmark.

That's now real routing, via `provideRouter`:

| Route | Component | Guarded | Loading |
|---|---|---|---|
| `/login` | `LoginComponent` | no | eager (first screen almost everyone hits) |
| `/quotes` | `QuotesComponent` | yes | lazy |
| `/quotes/new` | `QuoteFormComponent` | yes | lazy |
| `/quotes/new-signal` | `QuoteFormSignalComponent` | yes | lazy |
| `/quotes/:id` | `QuoteDetailComponent` | yes | lazy |
| `/` | — | — | redirects to `/quotes` |
| `**` | `NotFoundComponent` (new) | no | lazy |

`/quotes/new` and `/quotes/new-signal` are declared before `/quotes/:id` in
[`app.routes.ts`](../day-13/quotes-ui/src/app/app.routes.ts) — a param route
matches any segment, `new` included, so the static routes have to win the
match first or `/quotes/new` would resolve as `:id = "new"`.

## Files touched

**New:**
- `src/app/app.routes.ts` — the route table above
- `src/app/guards/auth-guard.ts` — functional `CanActivateFn`
- `src/app/components/not-found/{not-found.ts,html,css}` — real 404 page

**Rewritten:**
- `src/app/app.ts` / `app.html` — no more `view` signal or `@switch`; now a
  navbar (shown only when logged in) wrapping `<router-outlet>`, plus a bare
  `<router-outlet>` for the logged-out case (so `/login`'s own full-page
  hero layout, or the 404 page, can render without navbar chrome)
- `src/app/components/quote-detail/quote-detail.ts` / `.html` — no longer a
  child fed a `[quoteId]` input; now reads the `:id` route param directly
  (see below)

**Edited:**
- `src/app/app.config.ts` — added
  `provideRouter(routes, withComponentInputBinding(), withViewTransitions())`
- `src/app/app.css` — removed the auth-page/hero rules (moved to
  `login.css`, since `LoginComponent` now owns its whole page instead of
  `app.html` wrapping it); added `text-decoration: none` to `.nav-link`
  now that three of the four are `<a>` tags instead of `<button>`
- `src/app/components/login/login.ts` / `.html` / `.css` — the hero markup
  and its CSS moved in from `app.html`/`app.css`; after a successful
  login *or* register, navigates to the guard's preserved `returnUrl` query
  param, falling back to `/quotes` only when there wasn't one
- `src/app/components/quotes/quotes.ts` / `.html` / `.css` — removed the
  `selectedId` signal, the embedded `<app-quote-detail>`, and the
  select/delete handlers that talked to it; quote cards are now
  `<a [routerLink]="['/quotes', quote.id]">` instead of buttons with a click
  handler
- `src/app/components/quote-form/quote-form.ts` — removed the
  `quoteCreated` output (nothing was listening to it anymore, since routes
  render standalone, not as a parent's child); on success, navigates to
  `/quotes` after the same 900ms delay the Day 15 fix already used, so
  "Quote added." still gets a frame to paint before the view changes
- `src/app/components/quote-form-signal/quote-form-signal.ts` — same
  `quoteCreated` removal; navigates to `/quotes` immediately on success
  (this form has no success message to protect - confirmed unaffected)

## Why `QuoteDetailComponent` changed the way it did

It used to take `quoteId: input<number | null>`, fed by
`QuotesComponent`'s local selection state. Under routing there's no parent
component doing that anymore - the router renders it standalone. It now
takes `id: input<string>()`, bound straight from the `:id` route param via
`withComponentInputBinding()`. Route params are always strings, so the
number conversion (and the NaN guard the brief asked for) happens inside
the component, not at the router boundary:

```ts
const numericId = Number(raw);
if (!Number.isFinite(numericId)) {
  this.state.set('not-found');
  this.quote.set(null);
  return; // never reaches this.quoteService.getQuoteById(...)
}
```

`Number('abc')` and `Number(undefined)` are both `NaN`, so both land here
before any HTTP call is made - the guard never distinguishes "missing" from
"garbage," it just refuses to call the API with anything that isn't a
number. A numeric id the backend doesn't have is a different, already-
existing path: the `'error'` state, unchanged, still showing "Could not
load that quote."

The stale-response guard from before routing existed is still there, just
keyed off the route param string instead of a parent-supplied input -
selecting a different quote fast enough that responses could resolve out
of order was a real bug before, and nothing about switching to routing
makes that race go away.

The `quoteDeleted` output was removed the same way `quoteCreated` was: with
no parent listening under routing, a successful delete now calls
`router.navigate(['/quotes'])` directly instead of emitting an event.

## Verification

Backend at `http://localhost:5296` (the dev SQLite file had been reset to
empty between sessions - restarted the dev process so EF Core's
startup migration re-created the schema, then seeded and later deleted
throwaway test data via the API; no backend code was touched). Frontend
served via `ng serve` at `http://localhost:4200`. Driven live with
Playwright (`playwright-core`, system Chrome) plus `axe-core` for
accessibility - not just read from source.

### Build - clean, zero warnings

`ng build --configuration production`, full chunk list, pasted verbatim:

```
Initial chunk files | Names             |  Raw size | Estimated transfer size
chunk-HV2T6C4O.js   | -                 | 261.15 kB |                70.85 kB
chunk-M7ZCMNRG.js   | -                 |  29.48 kB |                 6.66 kB
main-SUNQWXNE.js    | main              |  12.53 kB |                 3.72 kB
styles-2PFSQOPB.css | styles            |   1.47 kB |               683 bytes
chunk-C4QVWID5.js   | -                 |   1.15 kB |               522 bytes

                    | Initial total     | 305.78 kB |                82.43 kB

Lazy chunk files    | Names             |  Raw size | Estimated transfer size
chunk-UQ5IHZMR.js   | quote-form-signal |  39.92 kB |                10.82 kB
chunk-JL5UFKZH.js   | quote-form        |   6.48 kB |                 2.09 kB
chunk-KFKK7XO2.js   | quote-detail      |   5.00 kB |                 1.74 kB
chunk-U5DWJVSC.js   | quotes            |   4.81 kB |                 1.70 kB
chunk-WEMKTA4T.js   | not-found         |   1.89 kB |               718 bytes
chunk-3P6CRUGW.js   | -                 | 492 bytes |               492 bytes
```

`grep -ic "warning\|error"` on the full build log: **0**. Exit code **0**.

Reported honestly, not glossed over: the initial bundle grew from the
109.17 kB baseline to **305.78 kB raw / 82.43 kB estimated transfer**. That
growth is `@angular/router` itself plus the app's own routing wiring
landing in the initial bundle - there is no version of "add routing" that
doesn't cost something in the initial chunk, since the router has to be
present before it can decide what to lazy-load. What matters for the
lazy-loading requirement is the second table: `quote-form-signal`,
`quote-form`, `quote-detail`, `quotes`, and `not-found` all appear as
their **own separate named chunks**, not folded into `main`.

### Lazy loading - observed on the network, not assumed from the route config

Loaded `/quotes`, let it settle, *then* attached a network listener and
clicked into a quote. One new request landed:

```
chunk-3TAMUXYQ.js
```

(Dev-server chunk hashes don't carry the friendly names the production
build's summary table shows, but this fired exactly once, exactly at the
moment of the click, after the initial page had already gone idle - that's
the actual behavior lazy-loading is supposed to produce, not just the
route table declaring `loadComponent`.)

### Guard + returnUrl - not just "redirects to /login"

Logged out, navigated straight to `/quotes/3`:

```
redirected to: http://localhost:4200/login?returnUrl=%2Fquotes%2F3
```

Logged in from there:

```
landed on: http://localhost:4200/quotes/3
```

Not `/quotes`. The guard's `returnUrl` query param survived through to
`LoginComponent`'s post-login navigation.

### Invalid id - confirmed no request ever fires

Navigated to `/quotes/abc` with a request listener attached for the whole
navigation. Requests matching `/api/quotes/` containing `NaN` or `abc`:
**zero** (`[]`, captured directly from the listener, not inferred). The
not-found message rendered.

### Real 404 - existing error handling untouched

Navigated to `/quotes/999999` (a numeric id the backend doesn't have): the
same `"Could not load that quote."` message that existed before this task,
confirmed present. This path was deliberately *not* touched.

### Browser back - list → detail → back

`/quotes` → clicked a card → `/quotes/1` → `history.back()` → landed back
on `/quotes` with the quote grid visible again. Standard browser
navigation, not a custom back button - `provideRouter` gives this for
free, but "for free" was verified, not assumed.

### Zoneless re-render

Typed into the filter input on `/quotes` (unrelated to routing, but a
concrete zoneless-reactivity check while already there): the card count
went from 2 to 1 with no manual change-detection call anywhere in the
code. If routing had broken zoneless reactivity, the value on screen would
have stayed frozen even though the underlying signal changed - it didn't.

### Success-message fix from Day 15 - re-checked, not assumed to still hold

The delayed-navigate pattern moved from `App.onQuoteCreated()` into
`QuoteFormComponent` itself. Re-ran the exact live check from Day 15:
submitted a valid quote on `/quotes/new`, and "Quote added." was visible
(`successVisibleBeforeNav: "Quote added."`) while still on `/quotes/new`,
*then* the URL changed to `/quotes` about 900ms later. The fix survived
the refactor; it wasn't just left in place hoping it would.

### Accessibility - re-ran axe-core, not re-read the markup

| Route | Violations |
|---|---|
| `/login` (logged out) | 0 |
| `/quotes` | 0 |
| `/quotes/new` | 0 |
| `/quotes/new-signal` | 0 |

Zero regressions against the prior 0-violation baseline. Every `<label
for>`, `aria-invalid`, `aria-describedby`, and `role="alert"` in the forms
was left untouched by this task - the only accessibility-relevant change
made was converting quote-card and nav-link elements from `<button>` to
`<a routerLink>`, which is a strict improvement for link semantics (real
`href`, works with open-in-new-tab, shows in the status bar on hover), and
axe confirms it didn't cost anything either.

## What could not be verified

- **The exact 47x-style numeric claim isn't applicable here**, but the
  honest equivalent - the initial-bundle size delta - is reported above
  rather than omitted, since it's a real cost of this change and the brief
  asked for what was actually observed.
- **Multi-tab / concurrent-session behavior** of the auth guard (e.g. one
  tab logging out while another is mid-navigation) wasn't exercised - out
  of scope for what was asked, but worth flagging as untested.
- **View transition animation quality** (`withViewTransitions()`) was
  confirmed to not break navigation or throw console errors, but the
  animation's visual smoothness wasn't judged - that's a subjective check
  better done by eye than by a script asserting a boolean.
- Dev database state: the SQLite file was found empty (0 bytes) at the
  start of this task, unrelated to any change here. Restarted the backend
  process to let EF Core's startup migration recreate the schema; seeded
  and then deleted throwaway quotes via the API to drive the live checks
  above, leaving the database empty again afterward, same as it was found.
  No backend source file was touched.
