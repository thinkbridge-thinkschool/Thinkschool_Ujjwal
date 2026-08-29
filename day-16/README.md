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

---

# Task 2 — State management

Same branch scope as Task 1: only `day-13/quotes-ui` and this README.
No new commits pushed as part of writing this section.

## The problem

Before this task, `QuotesComponent`, `QuoteDetailComponent`,
`QuoteFormComponent`, and `QuoteFormSignalComponent` each called
`QuoteService` independently and held their own copy of whatever data
they needed. Concretely, that meant: creating a quote on `/quotes/new`
had no way to tell `QuotesComponent` a new quote existed, so the list
only ever showed it after a full page reload; deleting a quote from
`/quotes/:id` had no way to tell the list to drop it either. Four
components, four independent sources of truth for the same data, no
propagation between them.

## What changed

New file:
[`src/app/store/quotes-store.ts`](../day-13/quotes-ui/src/app/store/quotes-store.ts)
- `QuotesStore`, `providedIn: 'root'` (a singleton - the same instance is
  injected everywhere, which is *the entire mechanism* that makes this
  work; there's no explicit "sync" step anywhere, just one shared signal
  every component reads).
- Private writable signals (`_quotes`, `_status`, `_error`, `_filter`),
  public readonly views (`.asReadonly()`) plus a `filteredQuotes`
  computed - callers can read state but can't set it except through the
  actions below.
- Actions: `load()`, `create(request)`, `remove(id)`, `setFilter(value)`,
  plus `loadOne(id)` and `getById(id)` (see the detail-component decision
  below).

Migrated to consume the store instead of `QuoteService` directly, with
their own local duplicated state removed:
- `QuotesComponent` - dropped its own `quotes`/`loadState`/`filter`/
  `filteredQuotes` signals entirely; reads `store.quotes()`,
  `store.status()`, `store.filteredQuotes()`, calls `store.load()` once
  and `store.setFilter()` on input.
- `QuoteDetailComponent` - dropped the local `quote` signal; it's now a
  `computed` reading from `store.getById()`. Local `state` signal stays
  (see below).
- `QuoteFormComponent` / `QuoteFormSignalComponent` - swapped
  `quoteService.createQuote(...)` for `store.create(...)`; both still own
  their own submitting/error UI exactly as before.

New test:
[`src/app/store/quotes-store.spec.ts`](../day-13/quotes-ui/src/app/store/quotes-store.spec.ts)
- 8 tests, including the two concurrency proofs described below.

`QuoteService` itself is untouched - the store wraps it, it doesn't
replace it. No HTTP call, validator, or interceptor changed.

## Design decisions

### `create()` / `remove()` return the Observable rather than owning the UI state

`QuotesStore.create()` and `.remove()` are thin wrappers:
`this.quoteService.createQuote(request).pipe(tap(quote => this._quotes.set([...this._quotes(), quote])))`.
They return the Observable rather than subscribing internally, so
`QuoteFormComponent`'s existing field-level server-error mapping,
`QuoteFormSignalComponent`'s Signal Forms `submit()` action, and
`QuoteDetailComponent`'s delete-confirmation flow all keep subscribing
exactly as they did before - only the object they call moved. The
store's only responsibility is the cache side effect on success; it
deliberately does not have an opinion on what a form does with a
field-level 400.

Both replace the array (`this._quotes.set([...current, quote])` /
`.set(current.filter(...))`) rather than mutating it in place. Under
zoneless, `array.push()` or `array.splice()` change the object's
*contents* without changing its *identity* - `signal()`'s default equality
check sees the same reference and skips notifying anything, so nothing
re-renders. This bit `QuotesComponent`'s original delete handler before
routing existed too; it's the same rule, just applied in one place now
instead of four.

**Neither triggers a refetch**, by design - the whole point of caching a
list is that adding or removing one row doesn't require re-asking the
server for all of them again. Verified live: see "Create... without a
refetch" below.

### Detail component: reads from the cache, with a fallback fetch for the deep-link case

`QuoteDetailComponent.quote` is a `computed(() => store.getById(numericId))`
- it reads from the shared cache, not a private copy. Chosen over "always
fetch" because the common path (arriving from the list, where the quote
is already cached) should cost nothing extra - `QuotesStore.loadOne(id)`
checks the cache first and only calls `GET /api/quotes/{id}` when the id
genuinely isn't there yet, which is exactly the deep-link case
(`/quotes/3` typed directly, or opened in a fresh tab, with the list
never loaded). A successful fallback fetch is merged into the shared
cache too, so a later visit to `/quotes` in the same session won't
re-fetch that row either.

The component's own `state` signal (`'loading' | 'loaded' | 'error' |
'not-found'`) stays local rather than moving into the store. That's a
deliberate line: `quotes`/`status`/`error`/`filter` in the store describe
*the list* - shared data multiple screens need to agree on. Whether *this
particular screen's* fetch attempt is in flight is specific to one
component's one request lifecycle, not data another screen would ever
need to read. Putting it in the store would make the store's `status`
field ambiguous - is it describing the list load, or whichever detail
fetch happened most recently? Keeping it local avoids that.

### Concurrency: a token guard, generalized from the existing pattern

`QuotesStore.load()` bumps a private `loadToken` counter and captures it
in the closure; a response only gets applied if the token still matches
`this.loadToken` when it arrives. This is the exact same discipline
`QuoteDetailComponent`'s fetch effect already used before this task
(comparing `this.id() === raw` before applying a stale response) -
generalized from "guard one component's one field" to "guard the store's
shared list," because the store makes the failure mode worse: before,
a stale response could only corrupt one screen's local copy; now, since
every screen reads the same signal, a stale response applied to the
store would corrupt what *everyone* sees.

Verified with a deterministic unit test
(`quotes-store.spec.ts`), not a live-browser timing race. `load()` was
called twice, and the response belonging to the *second* call was
flushed through `HttpTestingController` *first* - a real, not
hypothetical, out-of-order scenario (nothing guarantees network replies
arrive in request order). The first call's response was then flushed
second, and asserted to have no effect: the store still held the second
call's data. A live browser can't reliably force responses to arrive out
of order on demand, so a unit test that controls flush order directly is
the more rigorous tool here, not a lesser substitute for one.

Worth stating plainly: the current UI has no way to trigger two
overlapping `load()` calls through normal use - `QuotesComponent` only
calls `load()` when `store.status() === 'idle'`, so revisiting `/quotes`
never re-issues it. The guard is defensive code for callers this UI
doesn't currently have (a future refresh button, a programmatic caller,
a different component also depending on the store) rather than a bug fix
for something reachable today - which is exactly why the unit test,
not a live click-path, is what proves it works.

## Verification

Same setup as Task 1: backend at `localhost:5296`, frontend via `ng
serve` at `localhost:4200`, driven live with Playwright + system Chrome,
axe-core for accessibility. The dev database already had 10 real quotes
in it this time (unrelated prior session data) - live checks that needed
an empty or erroring list used Playwright's request interception to mock
just that one response, rather than deleting real data to force the
condition.

### Automated - `quotes-store.spec.ts`, 8 tests

```
Test Files  3 passed (3)
     Tests  20 passed (20)
```
(12 from Day 15 + 8 new.) Includes the two out-of-order concurrency
proofs described above.

### Build - clean, zero warnings, lazy chunks from Task 1 intact

`ng build --configuration production`: exit 0, zero warnings. Same five
lazy chunks as Task 1 (`quotes`, `quote-detail`, `quote-form`,
`quote-form-signal`, `not-found`), still separate from `main`. Initial
bundle: 305.79 kB raw / 82.40 kB estimated transfer - within rounding of
Task 1's 305.78 kB, meaning the store itself adds negligible weight to
the initial bundle (it's small, and mostly pulled into the lazy chunks
that already depend on it).

### Empty state - message renders, not a blank region

Mocked `GET /api/quotes?...` to return `[]`. Result: `"No quotes found."`
rendered (`emptyStateMessage: 1` - a real element matched, not inferred).

### Loading and error states

Mocked a 1.5s-delayed response: `"Loading quotes..."` was visible mid-flight
(`loadingStateMessage: 1`). Mocked a `503`: after `retryInterceptor`
exhausted its retries, the error case rendered
**`"Something went wrong. Please try again."`** - `AppHttpError`'s
generic fallback message, read via `store.error()?.friendlyMessage`, not
a hardcoded string in the template (the pre-store version had one:
`"Could not load quotes. Is the API running?"` - that copy is gone now,
replaced by whatever the real error actually says).

### Create → navigate to /quotes → present without a refetch

This required fixing my own test script first, worth recording: the
first attempt used `page.goto('/quotes/new')` to move between screens,
which is a full page reload in Playwright - that destroys and recreates
the singleton store, silently defeating the entire test (the "new" store
instance had never loaded anything, so `QuotesComponent` correctly
issued a fresh `load()`, and my script would have wrongly reported that
as a bug in the app). Fixed by clicking the actual "Create quote"
nav-link instead, the same way a real user reaches that screen.

With that fixed: created a quote from `/quotes/new`, navigated back to
`/quotes` via the app's own post-create redirect. **Zero** requests to
`GET /api/quotes` fired during the whole flow
(`getQuotesRequestsDuringCreateFlow: []`, from a live request listener,
not assumed). Card count went 25 → 26, and the new quote was visible by
name.

### Delete → back to list → gone without a refetch

Opened the just-created quote's detail page (SPA navigation), deleted
it, confirmed the browser dialog. **Zero** `GET /api/quotes` requests
fired (`getQuotesRequestsDuringDeleteFlow: []`). Back on `/quotes`, the
deleted quote's card was gone (count back to 25). Net effect on the real
database: zero - the quote created for this test was also deleted by the
end of it, confirmed via a direct API call afterward.

### Deep-link `/quotes/3` with no prior list load

Fresh browser context (no cookies/state carried over), logged in, and
navigated straight to `/quotes/3` - the list was never visited in this
context. The quote still rendered (`deepLinkQuoteVisible: 1`), proving
`loadOne()`'s cache-miss fallback to `GET /api/quotes/{id}` actually
fires and works, not just that the cache-hit path does.

### Zoneless re-render after a store mutation

Filtering the list (`store.setFilter()`) moved the visible card count
from 25 to 1 with no manual change detection anywhere in the code -
confirms the store's signals propagate correctly under
`provideZonelessChangeDetection()`. Create and delete above are the same
proof for `create()`/`remove()`'s array-replacement specifically (a count
that visibly changes on screen without a page reload *is* the zoneless
re-render check for those two actions).

### Accessibility - re-ran axe-core, zero violations

| Route | Violations |
|---|---|
| `/login` | 0 |
| `/quotes` | 0 |
| `/quotes/new` | 0 |
| `/quotes/new-signal` | 0 |

Matches the Task 1 baseline. Nothing about this task touched
`<label for>`, `aria-invalid`, `aria-describedby`, `role="alert"`, or
focus styling in any template - the only markup change was
`quotes.html`'s error case switching from a hardcoded string to
`{{ store.error()?.friendlyMessage }}`, and adding an `'idle'` case that
renders the same loading markup as `'loading'` (so every reachable status
has a rendered case - no silent blank region if `ngOnInit` somehow ran
before `status` flipped to `'loading'`).

## What could not be verified

- **A true network-level race** (two real HTTP requests to the real
  backend, response order not controlled) wasn't attempted live, for the
  reason stated above: it's not reliably forceable over a real network,
  and the deterministic unit test is the correct tool for proving ordering
  logic, not a live approximation of it.
- **Cache staleness over a long session** - if the *same* quote were
  edited by a second browser tab or a second user, this store has no
  mechanism to detect that and would keep serving its own cached copy.
  Not a bug relative to what was asked (there's no edit feature in this
  app yet), but worth naming as a real limitation of a single client-side
  cache with no invalidation strategy.
- **`getById`'s reactivity through nested calls** - `QuoteDetailComponent`'s
  `quote` computed calls `store.getById()`, a plain method, not a signal;
  Angular's `computed()`/`effect()` still track the underlying `_quotes()`
  read that happens inside it (confirmed by the live create/delete tests
  above changing what detail would show), but this wasn't isolated with a
  unit test specifically targeting that mechanism - only observed as a
  side effect of the live checks working.

## The NgRx threshold rule (DRAFT - for you to rewrite)

This is a first pass, explicitly marked as a draft. Rewrite it in your
own words - the goal here is naming testable signals, not the specific
phrasing.

**Move past plain signals (to a signal-store package, or NgRx) when *two or
more* of these become true at once - not when any single one does:**

1. **More than ~3 features need to read or write the same slice of
   state.** One shared store per feature (like `QuotesStore` here) is
   fine indefinitely. The threshold is when *unrelated* features start
   needing to coordinate through shared state - e.g. if this app grew a
   "Collections" feature that needed to react to quotes being deleted, a
   "notifications" feature that needed to know about every store's error
   state, and an "activity log" feature recording every mutation across
   both. Three-plus independent features reading/writing the same state
   is the point hand-wired signals stop being obviously simpler than a
   framework.
2. **Time-travel debugging or state-change replay is a real ask, not a
   nice-to-have.** NgRx's DevTools integration (action log, jump-to-state,
   diffing) exists because *actions* are a first-class, serializable
   record of everything that happened. Plain signals have no equivalent -
   you can inspect current state, not the sequence that produced it. If
   debugging a production issue genuinely requires reconstructing "what
   sequence of user actions led here," that's a concrete, testable reason
   plain signals stop being enough - not "it would be nice to have."
3. **An effects/middleware layer is needed** - meaning: side effects that
   need to be intercepted, retried, cancelled, or composed *across*
   multiple actions from *different* parts of the app (e.g. "cancel the
   in-flight search whenever navigation happens, regardless of which
   component triggered either one"). A single store's own methods
   (like `QuotesStore.load()`'s token guard) can handle side effects
   *local to that store* just fine. The threshold is cross-cutting effects
   that don't belong to any one store.
4. **Team size / onboarding cost.** NgRx's actions/reducers/effects/
   selectors give a large team one enforced shape for "how state changes
   happen," which pays off once enough people are touching the same state
   that an agreed convention beats each person's own judgment. Below
   roughly 5-8 engineers actively working in the same state layer, that
   enforced structure is often pure overhead - the convention is only
   valuable once there are enough people to disagree.

**Where this app sits against that rule right now:** one feature
(`quotes`) has one store. Nothing else in the app reads or writes quotes
state. There is no debugging requirement that's asked for action replay.
The only cross-cutting "effect" in this app is the interceptor pipeline
(auth/error-mapping/retry), which already lives at the HTTP layer, not
inside the store, and doesn't need to change per-feature. Team size is
effectively one. **Zero of the four signals are true, let alone two** -
this app isn't close to the threshold, and adding NgRx today would mean
adopting actions/reducers/effects/selectors to coordinate a single store
that nothing else needs to coordinate with. That's the concrete argument
for "not yet," not "NgRx is overkill" as a vibe.
