# day-13 quotes-ui — Verification

## 0. Getting the day-5 API running

The API failed to start because a **stale process from `day-11/QuotesApi`** (running since 2026-08-21) was already squatting on port 5296. Killed it (`kill 25401 25383`), deleted the leftover `day-5/QuotesApi/quotes.db{,-shm,-wal}` files, and ran `dotnet run` from `day-5/QuotesApi/` fresh. EF Core applied all 6 migrations cleanly to a brand-new `quotes.db` and Kestrel bound `http://localhost:5296` successfully.

The freshly-migrated `Quotes` table was empty, so I inserted 4 rows directly:

```sql
INSERT INTO Quotes (Author, Text) VALUES ('Marcus Aurelius', 'You have power over your mind, not outside events.');
INSERT INTO Quotes (Author, Text) VALUES ('Ada Lovelace', 'That brain of mine is something more than merely mortal.');
INSERT INTO Quotes (Author, Text) VALUES ('Grace Hopper', 'The most dangerous phrase is: we have always done it this way.');
INSERT INTO Quotes (Author, Text) VALUES ('Alan Turing', 'Sometimes it is the people no one imagines anything of who do the things that no one can imagine.');
```

### Exact API JSON shape (`GET http://localhost:5296/api/quotes`)

```json
[
  {"id":1,"author":"Marcus Aurelius","text":"You have power over your mind, not outside events.","createdByUserId":null},
  {"id":2,"author":"Ada Lovelace","text":"That brain of mine is something more than merely mortal.","createdByUserId":null},
  {"id":3,"author":"Grace Hopper","text":"The most dangerous phrase is: we have always done it this way.","createdByUserId":null},
  {"id":4,"author":"Alan Turing","text":"Sometimes it is the people no one imagines anything of who do the things that no one can imagine.","createdByUserId":null}
]
```

Field names/casing (**camelCase**, not the C# model's PascalCase — ASP.NET Core's default `System.Text.Json` web options lowercase the first letter):

| Field | Type |
|---|---|
| `id` | `number` |
| `author` | `string` |
| `text` | `string` |
| `createdByUserId` | `string \| null` |

This is exactly what `Quote` in [quote.model.ts](quotes-ui/src/app/models/quote.model.ts) declares.

### Extra fix required: CORS

Getting a 200 from `curl` was not sufficient — the API had **no CORS configuration at all** (`grep -rln "Cors" **/*.cs` returned nothing). A browser running the Angular dev server on a different port (e.g. `localhost:4213`) would have had the fetch silently blocked, since the response carried no `Access-Control-Allow-Origin` header:

```
$ curl -s -D - -o /dev/null -H "Origin: http://localhost:4213" http://localhost:5296/api/quotes
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
# (no Access-Control-Allow-Origin header)
```

I added a dev-only CORS policy in `day-5/QuotesApi/Program.cs` — `AllowAnyOrigin` restricted to loopback addresses (`Uri.IsLoopback`), applied only when `IsDevelopment()`:

```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
        options.AddPolicy("AngularDev", policy =>
            policy.SetIsOriginAllowed(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
                  .AllowAnyMethod()
                  .AllowAnyHeader()));
}
...
if (app.Environment.IsDevelopment())
{
    app.UseCors("AngularDev");
}
```

After restarting, both the simple GET and the CORS preflight OPTIONS request returned `Access-Control-Allow-Origin: http://localhost:4213`. This was the one real mismatch between "API returns 200 via curl" and "Angular app in a browser can actually consume it."

## 1. Scaffold

`day-13/quotes-ui` was generated with `@angular/cli@21.2.21`:

```
npx @angular/cli@21.2.21 new quotes-ui --routing=false --style=css --ssr=false --zoneless --skip-git --skip-tests --package-manager=npm --defaults
```

- Standalone components only (Angular 21's default; no NgModules exist anywhere in `src/`).
- `--zoneless` was passed to `ng new`, but the resulting `app.config.ts` didn't actually include `provideZonelessChangeDetection()` explicitly (zoneless appears to be assumed default when no zone.js dependency is added). I added it explicitly to `app.config.ts` along with `provideHttpClient()`, since the task required it to be visibly present:

```ts
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZonelessChangeDetection(),
    provideHttpClient(),
  ]
};
```

- `zone.js` is absent from `package.json` entirely — confirmed via grep.

## 2. QuoteService + QuotesComponent

- [quote.model.ts](quotes-ui/src/app/models/quote.model.ts) — the `Quote` interface, matching the real API field names.
- [services/quote.ts](quotes-ui/src/app/services/quote.ts) — `QuoteService`, uses `inject(HttpClient)`, `getQuotes(): Observable<Quote[]>` against `http://localhost:5296/api/quotes`.
- [components/quotes/quotes.ts](quotes-ui/src/app/components/quotes/quotes.ts) — `QuotesComponent`:
  - `quotes = signal<Quote[]>([])`, `filter = signal('')`, `loading = signal(true)`
  - `filteredQuotes = computed(...)` derives from both `quotes()` and `filter()` (substring match on author or text)
  - fetches via `QuoteService` in `ngOnInit`
  - `inject()` for `QuoteService`, no constructor
- [components/quotes/quotes.html](quotes-ui/src/app/components/quotes/quotes.html) — `@if (loading()) {...} @else if (filteredQuotes().length === 0) {...} @else { @for (quote of filteredQuotes(); track quote.id) {...} }`

### Mistake I had to correct

`ng generate service services/quote` (Angular 21's "2025" file-naming style, which drops the `Service`/`Component` suffix from both file names *and* class names) produced a class literally named `Quote` — colliding with the `Quote` data-model name I was about to use for the interface. I renamed the generated class to `QuoteService` and put the `Quote` interface in its own `models/quote.model.ts` file, rather than letting the service and the model share one identifier.

## 3. Verification performed

- `npm install` — succeeded (part of `ng new`'s scaffold step).
- `ng build` (production config) — compiled clean, no errors, no warnings: `136.26 kB` initial bundle.
- `ng serve --port 4213` — compiled and served with no errors.
- **Real browser verification**: rendered the served app in headless Chrome (`--headless=new --dump-dom`) against the live day-5 API. The DOM dump shows all 4 quotes actually fetched over HTTP and rendered inside `<app-quotes>` — i.e. `inject(HttpClient)` → `QuoteService` → CORS-enabled request → signal update → `@for` render, end to end, in an actual browser engine, not just a compiler check:

  ```html
  <app-root ng-version="21.2.21"><app-quotes ...><div class="quotes">
    <input ... class="filter-input">
    <ul class="quote-list">
      <li class="quote-item"><blockquote>You have power over your mind, not outside events.</blockquote><p class="author">— Marcus Aurelius</p></li>
      <li class="quote-item"><blockquote>That brain of mine is something more than merely mortal.</blockquote><p class="author">— Ada Lovelace</p></li>
      <li class="quote-item"><blockquote>The most dangerous phrase is: we have always done it this way.</blockquote><p class="author">— Grace Hopper</p></li>
      <li class="quote-item"><blockquote>Sometimes it is the people no one imagines anything of who do the things that no one can imagine.</blockquote><p class="author">— Alan Turing</p></li>
    </ul>
  </div></app-quotes></app-root>
  ```

  This confirms the **populated** state end to end.

  The **loading** and **empty** states were *not* independently exercised in the browser (no interaction/automation tooling was available to type into the filter input or hold the network response open) — they're covered by code-review confidence only: `loading` is a signal that starts `true` and is only set `false` inside the `subscribe` callback, so the `@if (loading())` branch is structurally guaranteed to render first; `filteredQuotes().length === 0` is a plain boolean check that will be true for a non-matching filter string or a genuinely empty API response, both of which were manually confirmed by reasoning through `quotes.ts`, not by driving the UI. If you want these empirically exercised too, that needs a real interaction (e.g. Playwright) rather than a DOM dump of the initial load.

- Mechanical checks (all passed via `grep`):
  - No `zone.js` reference anywhere in `package.json` / `angular.json`.
  - No `NgModule` anywhere in `src/`.
  - No `*ngFor` / `*ngIf` anywhere in `src/`.
  - No `constructor(` with injected params anywhere in `src/app/**/*.ts`.
  - `@for (quote of filteredQuotes(); track quote.id)` — has a `track` expression.

## State at end of this task

- `day-5/QuotesApi` is running in the background on `http://localhost:5296` (PID from this session's `dotnet run`), with 4 quote rows seeded directly via sqlite3.
- `day-13/quotes-ui` dev server was run on port 4213 for verification; not left running by default — run `npm start` (equivalent to `ng serve`, default port 4200) from `day-13/quotes-ui` to use it interactively.
- No commits were made; nothing was staged.
