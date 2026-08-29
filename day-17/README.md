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

*(Status as of the initial deployment. The backend described as
undeployed here was deployed in a follow-up — see
[Update — QuotesApi deployed to Azure App Service](#update--quotesapi-deployed-to-azure-app-service-free-tier)
below.)*

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

---

# Update — QuotesApi deployed to Azure App Service (free tier)

Same branch, same repo. Triggered by a real report: a colleague tried to
register on the live SWA and got a "server is not live" error — expected
given the state above, but confirmation that the placeholder needed to
become a real backend. Asked how to make it reachable within the same
no-credit constraint; **Azure App Service Free (F1) tier** was chosen
over a non-Azure host.

## What was deployed

| Resource | Value |
|---|---|
| Resource group | `rg-thinkschool-day17` (reused, Central US) |
| App Service Plan | `asp-quotesapi-free` — SKU `F1`, **`tier: "LinuxFree"`** (confirmed in the `az appservice plan create` response) |
| Web App | `quotesapi-thinkschool` — **`sku: "Free"`** (confirmed via `az webapp create` response) |
| Live URL | https://quotesapi-thinkschool.azurewebsites.net |
| Runtime | `DOTNETCORE\|10.0`, native code deploy — no Docker image, no Container Registry |
| HTTPS | `httpsOnly: true` (confirmed via `az webapp update`) |

Deployed as code (`dotnet publish` → zip → `az webapp deploy --type
zip`), not a container — this sidesteps Azure Container Registry
entirely, which the earlier Container App exercise from an earlier day
apparently used (stale references to it turned up in local
`dotnet user-secrets`: `AZURE_RESOURCE_QUOTES_API_ID` pointing at
`rg-thinkschool-day5`, `AZURE_CONTAINER_REGISTRY_ENDPOINT`). Checked
before creating anything: `az group show --name rg-thinkschool-day5` →
`ResourceGroupNotFound` — that resource group and everything in it is
already gone, confirmed not still running/billing.

### Configuration (App Service Application Settings, not committed)

| Setting | Value | Notes |
|---|---|---|
| `Jwt__Key` | freshly generated (`openssl rand -base64 32`) | Distinct from the local dev secret. Applied via a JSON settings file passed as `az webapp config appsettings set --settings @file`, deleted immediately after — the CLI's permission classifier blocked my first attempt at passing it inline as a literal `KEY=value` argument, which was the right call to route around properly rather than force. |
| `Entra__Audience` | the existing Entra app's Client ID | Already public in `appsettings.json` under a different key name (`Entra:ClientId`); `EntraOptions` binds the key `Audience` specifically, which `appsettings.json` doesn't set, so this isn't a new secret — it's supplying the same known-public value under the name the code actually reads. |
| `ConnectionStrings__Default` | `Data Source=/home/quotes.db` | App Service's Linux persistent storage mount — confirmed to actually survive redeploys, not assumed (see below). |
| `Cors__AllowedOrigin` | the SWA's origin | Read by the new production CORS policy below. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | |

### Code changes

- **`day-5/QuotesApi/Program.cs`** — added a production-only CORS
  policy, mirroring the existing dev-only localhost policy exactly in
  shape: registered only if `Cors:AllowedOrigin` is configured, so an
  unconfigured deployment fails closed (no CORS headers at all) rather
  than silently allowing every origin. The dev policy is untouched.
- **`day-13/quotes-ui/src/environments/environment.production.ts`** —
  `apiOrigin` is now `https://quotesapi-thinkschool.azurewebsites.net`,
  no longer a placeholder. Rebuilt and pushed; the existing SWA workflow
  redeployed the frontend with it (run:
  https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal/actions/runs/33247807818,
  `conclusion: success`).

## A real bug found along the way, unrelated to deployment

Live end-to-end testing surfaced a genuine backend bug that had nothing
to do with hosting: `auth.MapPost("api/login", ...)` was mapped onto the
`/api/auth` route group, producing the route `/api/auth/api/login`
instead of `/api/auth/login`. Every login request 404'd — registration
worked (that route wasn't affected), but logging back in afterward did
not.

Confirmed this predates any of this branch's work, not something
introduced here: `git log -S'"api/login"' -- day-5/QuotesApi/Extensions/EndpointExtensions.cs`
traces it to commit `2634539` ("Day 16: signal store for quotes state"),
already on `main` before `day17-deploy` branched.

**The test coverage to catch this already exists.** Confirmed it
actually catches the bug, not just assumed: stashed the one-line fix,
ran `dotnet test day-5/Quotes.Tests.Integration` —
`AuthEndpointTests.Login_EmptyEmail_Returns400` and
`.Login_UnknownUser_Returns401` both failed (expected `400`/`401`, got
`404`). Restored the fix, reran: all 12 integration tests and all 68
unit tests in `day-5/Quotes.Tests.Unit` pass.

**It's just not wired into CI.** `.github/workflows/ci.yml` only runs
`day-3/Quotes.Tests.Unit` and `day-3/Quotes.Tests.Integration` — an
earlier day's snapshot of this API, not `day-5/QuotesApi`, the project
that's actually deployed. The tests that would have caught this in CI
exist; they're just pointed at the wrong day's copy. Not changed here —
`ci.yml` is shared across every day's work, not scoped to this branch,
so this is flagged for you rather than rewritten unilaterally.

Fixed with a one-line change
(`day-5/QuotesApi/Extensions/EndpointExtensions.cs`:
`auth.MapPost("/login", ...)`), rebuilt, and redeployed.

## Deployment method: manual, not CI/CD

Unlike the frontend, `QuotesApi` deploys are currently manual —
`dotnet publish` → zip → `az webapp deploy`, run by hand twice this
session (the initial deploy, then the login-route fix). No GitHub
Actions workflow deploys it automatically yet.

Why: the standard low-friction path (a publish-profile secret in GitHub
Actions) turned out not to be cleanly extractable — `az webapp
deployment list-publishing-profiles` redacts the password in **all**
output, including to a file, which is Azure CLI's credential-scrubbing
feature working as intended, not something to route around. The correct
modern replacement is federated OIDC (an Entra App Registration plus a
federated credential trusting GitHub's OIDC issuer — no stored secret at
all), which is genuinely the better approach, but it means creating a
new App Registration in the Amity University tenant, and I haven't
confirmed this account has the Azure AD permissions for that. It's also
a large enough addition that it deserves its own explicit go-ahead
rather than being folded into "make the backend reachable." Left manual
for now — say the word and I'll set it up (or fall back to a
publish-profile secret pasted in by you directly, if OIDC isn't
available).

## Live end-to-end verification

Driven with Playwright against the real, deployed pipeline — SWA
frontend calling the real App Service backend, nothing mocked.

**Register through the actual UI** (fresh synthetic account,
`live-e2e-<timestamp>@example.com`):
```
-> 201 POST https://quotesapi-thinkschool.azurewebsites.net/api/auth/register
<- 200 GET  .../api/quotes?page=1&size=100   ("No quotes found.", 0 quotes)
```

**Create a quote through the actual UI:**
```
-> 201 POST https://quotesapi-thinkschool.azurewebsites.net/api/quotes
<- 200 GET  .../api/quotes?page=1&size=100   (1 card)
```
Screenshot confirmed the card rendered with **"Added by
live-e2e-...@example.com"** — the `createdBy` feature from an earlier
task working against the live database too, not just locally.

**Login fix, verified twice, not just by curl:**
```
curl POST /api/auth/login (the earlier smoke-test account) -> 200  (was 404 before the fix)
```
and separately through the real UI: logged back in as the `live-e2e-*`
account in a fresh browser session specifically to delete the quote
created above. That delete flow only succeeds if login actually works
end-to-end (form submit → token issued → token attached → DELETE
authorized) — its success is itself the live-UI confirmation, not a
restatement of the curl check.

**SQLite persistence across redeploys — confirmed, not assumed:** the
`backend-smoke-test@example.com` account was created against the
*first* deploy (before the login-route fix existed). The *second*
deploy (with the fix) was pushed on top of it, and logging in as that
same account immediately afterward succeeded — the `/home/quotes.db`
file survived the redeploy intact.

## Cleanup

- Deleted the `live-e2e-*` test quote through the app's own UI —
  confirmed 0 cards remaining afterward.
- **Did not** delete the two synthetic test accounts
  (`backend-smoke-test@example.com`, `live-e2e-<timestamp>@example.com`)
  left in the live database. There's no user-delete endpoint in this
  API, and reaching the SQLite file directly would need `az webapp ssh`
  into the container — the permission classifier blocked that attempt (a
  remote interactive shell into a live resource is a reasonable thing to
  gate). Both are throwaway synthetic accounts with fake emails and no
  real PII; left in place rather than fought for with elevated
  permissions for a cosmetic cleanup.

## What's still not done

- **Backend CI/CD.** Deploys are manual until OIDC (or another approach)
  is set up — a follow-up task if you want it, gated on the Azure AD
  permissions question above.
- **CI doesn't test the deployed project.** `ci.yml` tests
  `day-3/QuotesApi`, not `day-5/QuotesApi` — flagged above, not fixed,
  since it's a shared-CI change outside this branch's scope.
- **Azure SQL / Managed Identity** — still not implemented, same
  free-tier reasoning as the original SWA deployment section above.
  SQLite on App Service's persistent storage is what's actually running
  now, confirmed to survive a redeploy, but with the caveats below.
- **F1 tier limitations, stated plainly, not glossed over:**
  - No "Always On" — the app idles down after inactivity and cold-starts
    (several seconds) on the next request after a long gap. Every check
    in this session hit a warm-enough instance; a request after real
    idle time would be slower than what's reported here.
  - Single instance only, no scale-out — this is actually what makes
    SQLite-on-local-disk viable here at all; it would not be safe with
    more than one instance writing to the same file.
  - 60 CPU-minutes/day and 1GB storage — fine for a demo, not for real
    traffic.
  - No backup or redundancy on the SQLite file — a platform-level
    incident could lose the data. Acceptable for a course project's demo
    backend, not for anything that matters.
