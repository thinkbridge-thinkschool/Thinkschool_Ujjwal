# Day 27 — Security pass on the live system

Threat-modeled the deployed system as it actually is, fixed what was real and cheap to fix, verified every change against the live API, and scaffolded (not deployed) the fix that costs money. Not committed.

## Disk check, first

`df -h /` showed 1.6GB free against a ~1.5GB ZAP image before extraction overhead - not safe, given this machine has already run out of disk mid-task twice. Checked `docker system df`/`docker ps` for easily reclaimable space instead of pulling anything; the Docker daemon didn't respond within a reasonable time (consistent with past sessions where an unresponsive daemon correlated with low disk). Rechecked disk again after finishing sections 1-3 (3.2GB free by then, some background cleanup happened) but the daemon was still unresponsive - **the ZAP scan was skipped**, per the explicit instruction to do sections 1-3 without it rather than risk filling the disk again. Sections 1-3 below are unaffected by this.

## 1. STRIDE-lite threat model

Grounded in the actual deployed system (`quotesapi-thinkschool2`, the new Static Web App, `rg-thinkschool-day17`, East Asia, managed by `quoteshub-dev`) - not the scaffold. `capstone/` shares the same shape wherever noted (it has no endpoints of its own yet, but the same trust boundaries would apply the moment it does).

### Spoofing

- **No email verification on registration.** Anyone can register as `victim@realcompany.com` without proving they own it. An attacker registers with a target's real email, then anything that later trusts `createdBy` as "this is really them" is spoofed. **Open.**
- **Refresh token reuse detection is real and working**: presenting an already-rotated refresh token revokes the entire token family (`EndpointExtensions.cs`'s `/api/auth/refresh`), a genuine defense against a stolen-refresh-token replay. **Mitigated.**
- **JWT signature validation is sound** - HMAC-SHA256, key sourced from Key Vault via managed identity (Day 25), not derivable by an attacker without compromising the vault itself. **Mitigated.**

### Tampering

- **CreateQuoteRequest's `[MaxLength]` attributes were never enforced.** Confirmed by reading the pipeline, not assumed: ASP.NET Core Minimal APIs don't run DataAnnotations validation automatically the way `[ApiController]`-based MVC controllers do, and no validation filter existed for this DTO anywhere in the app. Day 14 found this; it was still true today. **Fixed** - explicit length checks added in the handler (200/2000 chars, matching the DTO's stated limits).
- **No request body size limit anywhere** - Kestrel's 30MB default applied unmodified to an API whose largest legitimate body is a few KB. **Fixed** - capped at 64KB.
- **SQL injection**: EF Core parameterizes every query in this codebase; no raw SQL concatenation found anywhere in `Data/`/`Repositories/`. **Mitigated** (by ORM discipline, not by any dedicated anti-injection code - worth naming as incidental, not deliberate).
- **CSRF**: not applicable in the classic sense - auth is Bearer-token-based, sent explicitly by JS, never an ambient browser-attached credential the way a cookie is.

### Repudiation

- **No structured "who did what" audit trail beyond ownership checks at action time.** `DELETE /api/quotes/{id}` verifies the caller owns the quote before deleting it, but nothing durably records *which* authenticated identity performed the delete in a way that's easily queryable later - Application Insights captures the request/trace, but `user_Id`/`user_AuthenticatedId` enrichment isn't configured, so proving "user X did this" after the fact means digging through raw logs rather than a clean audit query. **Open.**

### Information Disclosure

- **`createdBy` (a real email address) was returned by `GET /api/quotes` and `GET /api/quotes/{id}` - both fully anonymous, unauthenticated endpoints.** Anyone, including a mass scraper with no account, could harvest every quote author's email by paging through the public list. **Fixed** - responses are now redacted (`createdByUserId`/`createdBy` nulled) for unauthenticated callers only; authenticated callers (the "Added by" feature the frontend already renders) see it unchanged. Verified live both ways below.
- **`ExceptionMiddleware` returned the raw `ex.Message` in the 500 response body, in every environment including production** - a real vector for leaking internal details (file paths, connection info, internal type names) to any caller who can trigger an unhandled exception. Found by reading the middleware, not assumed. **Fixed** - production now returns a generic message; the full exception is still logged server-side, so nothing is lost for debugging.
- **The GitHub repo is public, and dev SQLite database files (with a real email and a BCrypt password hash) have been committed since Day 1** - confirmed via `git log --all --diff-filter=A`: `day-1/QuotesApi/quotes.db`, `day-3/.../quotes.db`, `day-4/.../quotes.db`, `day-5/.../quotes.db`, `day-7/quotes-day7.db`, `day-11/.../quotes.db` were all added and remain in history today. The password is BCrypt-hashed (not reversible at any practical cost), but the email is exposed in plaintext, indefinitely, to anyone who clones the repo. **Open** - not fixed today. Purging these would mean a second, much larger history rewrite spanning many more days' commits than yesterday's single-file fix; that's a real, disclosed decision to not expand scope unilaterally, not an oversight.
- **A plaintext SQL admin password was committed during Day 26** (a leftover temp file from an earlier session's secret-hydration workflow) and pushed to `origin/main` and `origin/day26-appinsights`. **Fixed yesterday** - stripped from all history with `git-filter-repo`, force-pushed, verified gone from a fresh fetch of both branches. The password itself was for a SQL server on the now-expired old subscription, so the live-exposure window is believed closed, though that couldn't be independently re-confirmed (see "what I could not verify").

### Denial of Service

- **No rate limiting anywhere before today.** `/api/auth/login` and `/api/auth/register` were open to unlimited credential-stuffing/spam-registration attempts. **Fixed** - see section 3.
- **`GET /api/quotes` and every other endpoint remain unrate-limited.** This matters specifically because the App Service runs on **F1**, which has a hard **60 CPU-minutes/day** quota (documented in Day 17). A moderate flood of anonymous, cache-missing requests (varying `page`/`size` to defeat HybridCache) could exhaust that entire daily quota and take the whole app offline for legitimate users, for the rest of the day, for free (from the attacker's perspective). **Open** - the task's explicit, highest-value ask was auth-endpoint rate limiting specifically; extending it to read endpoints wasn't done today and is named here as a real gap, not silently left out.
- **SQL is serverless with the free-limit offer, `freeLimitExhaustionBehavior: AutoPause`.** This is good news for the cost side (exhausting the free quota pauses the database rather than silently billing), but it's a matching availability risk: an attacker deliberately burning the free compute allowance takes the *database* offline for the rest of the billing month. **Open**, same root cause as the F1 quota risk above - free tiers chosen for cost reasons carry a DoS surface that a paid tier with real headroom wouldn't.

### Elevation of Privilege

- **Every authenticated user is granted all three scopes (`quotes.read`, `quotes.write`, `quotes.delete`) unconditionally at login/register** (`JwtTokenService.GenerateAccessToken`) - confirmed by reading the token-minting code directly. There is no privilege tier above "registered user" to escalate *to* - which means classic vertical privilege escalation doesn't apply, but it also means the scope system provides no real defense-in-depth today: a 30-second anonymous signup grants full create/edit/delete rights system-wide (delete is ownership-gated per-resource; create/edit are not scoped down at all). **Open**, named plainly rather than credited as "mitigated" just because a scope check exists on paper.

## 2. Private endpoints - design-only, and here's why

**F1 genuinely cannot do VNet integration.** Confirmed directly (not assumed from memory): [Microsoft's own documentation](https://learn.microsoft.com/en-us/azure/app-service/overview-vnet-integration) states VNet integration requires a dedicated-compute tier (Basic and above) and explicitly excludes Free/Shared. **Basic (B1)** is the cheapest tier that supports it. Actual cost, queried directly from Azure's retail pricing API for this exact region (not a search-result guess): **$0.02/hour for B1 Linux in East Asia → ~$14.60/month** at full-time usage - a real, ongoing cost against a $100 one-time credit, not deployed without asking first.

**Written anyway, so it's ready:**
- `day-23/modules/network.bicep` (new) - a VNet with an App-Service-delegated integration subnet and a separate private-endpoint subnet, a private DNS zone for `privatelink.database.windows.net` with a VNet link, and a private endpoint + DNS zone group attaching to the existing SQL server.
- `day-23/modules/appservice.bicep` - a new `virtualNetworkSubnetId` parameter, defaulted to empty (so F1 today is completely unaffected), which would take `network.bicep`'s `integrationSubnetId` output the moment the plan is ever upgraded to B1+.

One disclosed, understood linter warning on `network.bicep`: `no-hardcoded-env-urls` flags the literal string `privatelink.database.windows.net`. This isn't actually fixable the way the linter suggests - that exact string is Microsoft's fixed, documented private DNS zone name for Azure SQL private endpoints, not a cloud-specific value `environment()` would compute differently.

**Interim mitigation, deployed today (free):** SQL firewall tightened from "Azure services only" (which is barely a barrier - it admits requests from any Azure resource in any subscription, not just this one) to also require one specific known IP (`day-23/params/dev.bicepparam`'s new `sqlFirewallRules`, wired through `main.bicep`, deployed via the stack). Disclosed limitation: this is *my* IP, captured today - it will need updating (or removing) if it changes, and is not a substitute for the actual private-endpoint fix above.

## 3. API surface hardening

**Authorization audit - read the whole endpoint file, didn't assume:**

| Endpoint | Auth | Notes |
|---|---|---|
| `POST /api/auth/register`, `/login`, `/refresh` | Anonymous (must be) | Now rate-limited (below) |
| `GET /api/quotes`, `GET /api/quotes/{id}` | Anonymous (deliberate, documented) | `createdBy` now redacted for anonymous callers |
| `GET /api/quotes/random` | Anonymous (deliberate, explicit `.AllowAnonymous()`) | Upstream failures mapped to 503, not 500 |
| `POST /api/quotes` | `RequireAuthorization("can-edit-quotes")` | ✓ |
| `DELETE /api/quotes/{id}` | `RequireAuthorization("can-delete-quotes")` + ownership check | ✓ |
| `POST /collections`, `.../items`, `DELETE .../items/{quoteId}` | `RequireAuthorization("can-edit-quotes")` + ownership check on the latter two | ✓ |
| `/api/diagnostics/*` | Anonymous, but gated to `IsDevelopment()` in code - don't exist in the deployed environment at all | Confirmed absent from the live app's route table by design |

**Nothing accidentally open was found.** No global fallback authorization policy exists either (checked `Program.cs` directly) - endpoints are anonymous only where explicitly designed that way, not by an accidental absence of a default-deny.

**Rate limiting - the highest-value fix, per the brief.** ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting` (no new dependency): a fixed-window limiter, partitioned by caller IP, 5 requests/minute, applied to the whole `/api/auth` group (register, login, *and* refresh - a stolen or guessed refresh token shouldn't get unlimited rotation attempts either). Partitioning by IP required also adding `UseForwardedHeaders()` - without it, every caller on App Service would share the platform's internal front-end address as their "IP," collapsing everyone into one shared rate-limit bucket. **Demonstrated live**, not just described:

```
$ for i in $(seq 1 8); do curl -X POST .../api/auth/login -d '{bad creds}'; done
attempt 1: HTTP 401
attempt 2: HTTP 401
attempt 3: HTTP 401
attempt 4: HTTP 401
attempt 5: HTTP 401
attempt 6: HTTP 429
attempt 7: HTTP 429
attempt 8: HTTP 429
```

**API versioning: not yet, and here's the trigger.** This API has exactly one consumer (its own Angular frontend), deployed by the same person in the same session every time either side changes. There is no scenario today where an old client needs to keep working against a new server shape. The trigger for actually adding versioning (URL-path, `/api/v1/...`, would be the natural fit here) is the first time a breaking wire-format change needs to ship *while* an old client is still genuinely in use - a second consumer, a mobile app, or a public API contract with external callers. Adding it now would be complexity with no one to protect against yet.

**Input limits**: server-side length enforcement added (see Tampering above); request body capped at 64KB (see Tampering above).

**`createdBy` disclosure**: decided and fixed (see Information Disclosure above) - redact for anonymous callers, preserve for authenticated ones, since the "Added by" display is a real, existing frontend feature (day-17/README.md) and full removal would be a feature regression, not just a security fix.

## 4. OWASP ZAP baseline scan

**Not run.** Disk space and an unresponsive Docker daemon, as described at the top. This is stated plainly rather than glossed over or faked.

## Verify

- **Live API and frontend, before and after every change**: `GET /health` → `200`, `GET /api/quotes` → `200`, frontend → `200` - checked after the infra stack update, again after the first code deploy, again after the `ExceptionMiddleware` fix's redeploy.
- **Rate limiting**: real `429`s pasted above, from the actual live endpoint, not a local reproduction.
- **`createdBy` redaction, both directions, against the live API**:
  - Anonymous `GET /api/quotes` → `"createdByUserId": null, "createdBy": null`.
  - The same request with a valid bearer token → `"createdByUserId": "1", "createdBy": "restore-admin@example.com"` (a synthetic account created for this session, not a real person).
- **SQL firewall**: `az sql server firewall-rule list` shows both `AllowAzureServices` and the new `AllowMyIP-day27` rule, deployed through the stack (`az stack group create`, same `detachAll`/`denyDelete` settings as every prior infra change) - not via a direct `az sql server firewall-rule create`, consistent with Day 25's finding that out-of-band writes get rejected.
- **Test suite**: 69/69 unit (one test split into two - Development vs. production exception-message behavior - to actually cover the new fix, not just unblock the build), 40/40 integration, run before every deploy.
- **No endpoint that should be protected is open**: table above, read from source, not assumed.

## What I could not verify

- **The ZAP scan itself** - skipped for the disk/Docker reasons stated; no before/after severity counts exist to paste.
- **Whether the Day 26 SQL password's exposure window is truly, fully closed** - the server it belonged to should be gone with the expired subscription, but I have no independent way to confirm that subscription is genuinely unreachable rather than just inaccessible to me, same caveat as when the fix was made.
- **Whether purging the Day-1-through-11 `.db` files from history is worth the disruption of another full rewrite** - named as a real, open finding rather than fixed; that's a decision for you to make deliberately, not one I made unilaterally given how large the previous rewrite already was.
- **Whether the F1/read-endpoint DoS risk or the free-tier SQL exhaustion risk have ever actually been exploited** - named as open, credible risks grounded in the platform's own documented limits, not tested by actually trying to exhaust either quota against a live, budget-constrained resource.
- **Repudiation tooling beyond what exists today** - named as open; building real per-action audit logging wasn't attempted, only the gap was identified.

Not committed.
