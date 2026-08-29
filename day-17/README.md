# Day 17 — Deploy quotes-ui to Azure Static Web Apps

All app code stays in [`day-13/quotes-ui`](../day-13/quotes-ui) — this
folder holds only the write-up, same pattern as Days 14–16. Branch:
`day17-deploy`.

## The brief

Deploy `day-13/quotes-ui` to Azure Static Web Apps on the **free tier
only** (no usable subscription credit), wire GitHub Actions so pushes
deploy automatically, and add the navigation fallback SPA routing needs
on a static host. `apiOrigin` was already extracted to
`src/environments/` in the prior task and is currently the placeholder
`https://REPLACE_WITH_PRODUCTION_API_ORIGIN` — this task explicitly does
not invent a real backend URL, so the deployed app is expected to show
its connectivity error state once past the login screen, not silently
work.

## What was deployed

**Live URL:** https://gentle-smoke-08c4a1d10.7.azurestaticapps.net

| Resource | Value |
|---|---|
| Resource group | `rg-thinkschool-day17` (Central US) |
| Static Web App | `swa-quotesui-thinkschool` |
| SKU | **Free** (confirmed via `az staticwebapp show`) |
| Resources in the RG | exactly one — the Static Web App itself (`az resource list`) |

No Container App, Azure SQL, App Service, or Key Vault was created.
`Microsoft.Web` had to be registered as a resource provider on the
subscription first (`az provider register`) — that's a subscription-level
capability flag, not a billable resource.

### Build

`ng build --configuration production` from `day-13/quotes-ui`, output
path confirmed from `angular.json` (no explicit `outputPath`, so the
`@angular/build:application` default applies): **`dist/quotes-ui/browser`**.
Clean build, zero warnings, same five lazy chunks as Day 16
(`quote-form-signal`, `quote-form`, `quote-detail`, `quotes`,
`not-found`) still separate from `main`.

### staticwebapp.config.json

Added at
[`day-13/quotes-ui/public/staticwebapp.config.json`](../day-13/quotes-ui/public/staticwebapp.config.json)
rather than the project root — Angular's `public/` folder is the assets
directory the CLI copies verbatim into the build output (per
`angular.json`'s `assets` glob), so this is the mechanism that actually
gets the config file into `dist/quotes-ui/browser` where SWA reads it.
Confirmed present in the build output after moving it there.

```json
{
  "navigationFallback": {
    "rewrite": "/index.html",
    "exclude": ["/*.{css,js,ico,png,svg,webmanifest}", "/assets/*"]
  }
}
```

Without this, SWA's static file server 404s on any path that isn't a
real file on disk — `/quotes/3`, `/quotes/new`, any deep link — because
Angular's router only exists once `index.html`'s JS has loaded and taken
over. The fallback rewrites everything not matching the excluded static
asset extensions back to `index.html`, letting the Angular router take
it from there.

### GitHub Actions

**New file:**
[`.github/workflows/azure-static-web-apps.yml`](../.github/workflows/azure-static-web-apps.yml)
— the only file this task added outside `day-13/quotes-ui` and
`day-17/`.

No other file was created or modified by the workflow setup. It does
not touch the existing `.github/workflows/ci.yml` (unrelated dotnet test
pipeline).

The workflow:
- Triggers on push to `day17-deploy` (path-filtered to
  `day-13/quotes-ui/**` and the workflow file itself), and on pull
  requests targeting that branch for preview environments.
- Runs `npm ci` + `ng build --configuration production` itself (Node 22,
  matching `@angular/cli`'s declared engine range), then deploys the
  **prebuilt** output via `Azure/static-web-apps-deploy@v1` with
  `skip_app_build: true` and `app_location:
  day-13/quotes-ui/dist/quotes-ui/browser` — SWA's own Oryx build step is
  bypassed entirely in favor of the build already verified above.
- Includes the standard `close_pull_request` job to tear down PR preview
  environments on close.

**Deployment token:** retrieved via `az staticwebapp secrets list` and
stored as the `AZURE_STATIC_WEB_APPS_API_TOKEN` GitHub Actions secret
(`gh secret set`, confirmed present via `gh secret list`). The workflow
only ever references it as `${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN }}`.

Grepped the workflow file for anything token-shaped before finishing:

```
$ grep -nE "AZURE_STATIC_WEB_APPS_API_TOKEN|secrets\.|[A-Za-z0-9_-]{40,}" .github/workflows/azure-static-web-apps.yml
43:          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN }}
44:          repo_token: ${{ secrets.GITHUB_TOKEN }}
59:          azure_static_web_apps_api_token: ${{ secrets.AZURE_STATIC_WEB_APPS_API_TOKEN }}
```

Only the `secrets.*` expression syntax appears — no literal value.
Separately, grepped the actual token value against the entire working
tree (`grep -rn "$TOKEN" .`): zero matches, exit code 1.

## Verification log

Everything below was actually run and observed, not assumed from
config.

### GitHub Actions run

Pushed the two new files to `day17-deploy`. Run:
**https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal/actions/runs/33245337566**
— `conclusion: success`, `Build and deploy` job completed in 1m31s, all
steps green (checkout → setup-node → `npm ci` → `ng build` → SWA
deploy).

### Live URL and deep links

```
GET /            -> 200
GET /quotes/3    -> 200
GET /quotes/new  -> 200
```

All three return `200` directly from the CDN, not a client-side-only
success masking a 404 — `curl -sI` on each shows a real `HTTP/2 200`
with SWA's response headers. This is what the navigation fallback is
for: without it, `/quotes/3` and `/quotes/new` would 404 at the edge
before Angular ever loads.

### Logged-out state and the auth guard

Drove the live site with Playwright (headless Chromium, installed for
this verification only — not added as a project dependency). Root URL
loaded and the auth guard redirected client-side to
`/login?returnUrl=%2Fquotes` before any quotes UI rendered; the login
form (`#login-email`, `#login-password`) was present and rendered
correctly (screenshot taken, hero copy and form both visible).

Hitting the two deep links directly while logged out produced the same
guard behavior, each preserving its own `returnUrl`:

```
/quotes/3   -> redirected to /login?returnUrl=%2Fquotes%2F3
/quotes/new -> redirected to /login?returnUrl=%2Fquotes%2Fnew
```

Both are still `200` responses that render the app shell and the login
form — the guard's redirect is a client-side navigation after the SPA
loads, not a server 404.

### Error state with no reachable API

`apiOrigin` is the placeholder host, which doesn't resolve. To reach
the code path that would only run once logged in, seeded a synthetic
session token directly into `localStorage` (matching `AuthService`'s
`quotes.auth` storage shape) rather than inventing a real backend to log
in against, then navigated to `/quotes`.

Result: no blank page, no stuck spinner. `.spinner` elements present
after settling: **0**. The store's real error path rendered:

```
Your quotes
0 quotes in the collection
Could not reach the server. Check your connection and try again.
```

Console showed three `net::ERR_NAME_NOT_RESOLVED` entries (the expected
failure mode for a placeholder host), and `errorMappingInterceptor` +
`retryInterceptor` correctly converted that into the same
connectivity-error copy verified against the backend contract in the
Day 15/16 work — confirming this isn't a new failure mode introduced by
deployment, it's the existing "API unreachable" path, now exercised for
real by a genuinely unreachable placeholder instead of a local backend
being stopped.

### Lighthouse (desktop preset, live URL)

| Category | Score |
|---|---|
| Performance | **95** |
| Accessibility | 97 |
| Best Practices | 100 |
| SEO | 82 |

Performance meets the ≥95 target. Reported as measured — nothing was
adjusted to hit the number. Top items Lighthouse actually listed:

- **First Contentful Paint** 1.0s (audit score 0.88) and **Speed Index**
  1.6s (score 0.81) are the two audits pulling Performance down from a
  perfect 100; both are normal cold-start numbers for a CDN-hosted SPA on
  first load, not a specific bug.
- **Reduce unused JavaScript** — est. savings of 31 KiB. Angular's
  initial chunk ships more than this single route strictly uses; this is
  the same initial-bundle cost already reported honestly in Day 16
  (routing itself has a footprint), not something introduced by
  deployment.
- **Network dependency tree** flagged as an insight, not a scored
  deduction — the render-blocking chain from `index.html` to the initial
  JS/CSS chunks.

SEO (82) and Accessibility (97) are informational, not the target
metric, but worth stating plainly rather than omitting:

- **`robots.txt` is not valid** and **missing meta description** are
  the two SEO deductions. The `robots.txt` one is a direct, verified
  side effect of the navigation fallback this task was required to add:
  `curl /robots.txt` returns `content-type: text/html` — the SPA's
  `index.html`, not a real robots file — because no `/robots.txt` file
  exists in the build output and the fallback rewrite (correctly) treats
  it like any other unmatched path. Adding a real `robots.txt` to
  `public/` would fix this but wasn't requested and touches app content,
  not deployment plumbing, so it was left alone.
- **Missing `<main>` landmark** is the one Accessibility deduction. This
  is pre-existing app markup (`app.html`'s router-outlet wrapper), not
  something this task touched — the constraints explicitly said not to
  change accessibility wiring, so it's reported, not fixed.

Full JSON/HTML Lighthouse reports were generated locally during this
session (not committed — they're a point-in-time artifact, not part of
the deployed app or its config).

## What could not be done, and why

**The backend is not deployed.** The Azure subscription has no usable
credit, and deploying `day-5/QuotesApi` for real would require at least
one billable compute resource (App Service, Container Apps, or similar)
plus a real database reachable from it — everything this task was
explicitly told to avoid. `apiOrigin` in
`src/environments/environment.production.ts` therefore remains the
placeholder `https://REPLACE_WITH_PRODUCTION_API_ORIGIN`, unchanged from
the prior task, and the live app's connectivity-error state (verified
above) is the correct, expected behavior of that placeholder — not a bug
to hide or a mock to paper over.

**Managed Identity — to the API, and from the API to Azure SQL — is
consequently unimplemented.** Both depend on the backend actually being
deployed to a compute resource that can hold an identity, which didn't
happen here. What the wiring would look like:

- **Static Web Apps cannot itself hold a Managed Identity for calling a
  server.** SWA's Free tier serves static files to the browser — HTML,
  JS, CSS — from a CDN edge; there is no server-side execution context
  in the deployed app for an identity to attach to. The `identity: null`
  field on the SWA resource created in this task reflects that directly
  (`keyVaultReferenceIdentity: "SystemAssigned"` is a fixed platform
  default for a feature not in use here, not evidence of an assigned
  identity). Any server-side call this app makes has to happen from
  somewhere else that *can* hold an identity:
  - **SWA Managed Functions** — an Azure Functions app that SWA
    provisions and links automatically (available on the Free tier,
    unlike linking an existing external Function App, which needs
    Standard). A function under `api/` could hold a managed identity and
    proxy calls to the real backend server-side, keeping any credential
    off the browser entirely. Not implemented here because there's still
    no real backend to proxy to.
  - **The backend itself using Managed Identity to reach Azure SQL** —
    this is the more direct fit for `QuotesApi`'s actual shape (an
    ASP.NET Core minimal API, not a Functions app). Deployed to App
    Service or Container Apps, it would get a system-assigned identity,
    be added as an Azure AD principal on the Azure SQL server, and
    connect via `Authentication=Active Directory Managed Identity` in
    the connection string instead of a SQL login and password — no
    credential stored in `appsettings.json` or an environment variable
    at all. This is the natural next step once compute + SQL are
    provisioned, but both are billable, so neither was created here.

## What was not verified

- **A real production `apiOrigin`** — there is nothing to point it at
  yet, so the "logged in, quotes actually load" path could not be
  exercised end-to-end on the deployed site. Every check above that
  needed an authenticated session used a synthetic local-storage token
  specifically to reach the error-state code path, not to fake a working
  backend.
- **PR preview environments** — the workflow's preview/close jobs are
  configured (standard Free-tier SWA behavior, up to 3 concurrent
  staging environments) but no pull request was opened against
  `day17-deploy` during this task, so that path only ran the `push`
  branch, not the `pull_request` triggers.
- **Custom domain / TLS beyond the default `*.azurestaticapps.net`
  certificate** — out of scope for this task and not attempted.
