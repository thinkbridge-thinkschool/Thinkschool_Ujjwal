# Day 23 — Bicep for QuoteHub's Azure resources

Infrastructure-as-code for what's actually running, plus what the app is designed to grow into. Nothing in this folder was deployed — every resource below already existed before this task, or exists only as a `what-if` preview.

## What's deployed vs. designed-for

| Resource | State | Module |
|---|---|---|
| Static Web App (frontend) | **Deployed** — Free tier, `rg-thinkschool-day17` | `modules/staticwebapp.bicep` |
| App Service (API) | **Deployed** — F1 free tier, Linux, .NET 10 code deploy | `modules/appservice.bicep` |
| Azure SQL | **Deployed** — see note below | `modules/sql.bicep` |
| Service Bus (topic + `audit`/`stats` subscriptions) | **Not deployed** — designed for | `modules/servicebus.bicep` |

**A correction to the brief, stated plainly rather than glossed over:** the brief describes Azure SQL as "not deployed but designed for," alongside Service Bus. That was true when this session started, but is no longer true — earlier in this same session I created `sql-quotesapi-thinkschool` (server) and `quoteshub` (database, free-tier serverless) in `rg-thinkschool-day17` while working on moving the app off SQLite. The *application code* has been switched to `UseSqlServer` and builds cleanly, but the live App Service has **not** been redeployed with it yet (that redeploy was interrupted by a local Docker/disk-space problem and hasn't been resumed) — so the currently-running production app still talks to its old SQLite file. `sql.bicep` and `params/dev.bicepparam` model the SQL server and database as they actually exist today, not as a hypothetical.

## Structure

```
day-23/
  main.bicep                 orchestrates all four modules
  modules/
    appservice.bicep         API host: Linux plan + web app
    sql.bicep                SQL logical server + database + firewall rules
    servicebus.bicep         namespace + topic + audit/stats subscriptions
    staticwebapp.bicep       frontend
  params/
    dev.bicepparam           cheapest tier per service - matches what's live today
    prod.bicepparam          realistic production tier per service
```

Every name, SKU, region, and size is a parameter — nothing is hardcoded in a module. `main.bicep` is the only place that wires modules together, using each module's own outputs (the SQL server's FQDN, the Static Web App's hostname) to build the API's connection string and CORS origin, rather than duplicating those values as separate literal parameters.

## Dev vs. prod

Same four modules both ways; only `params/*.bicepparam` differs.

| | Dev (`dev.bicepparam`) | Prod (`prod.bicepparam`) |
|---|---|---|
| App Service | F1 / Free, 1 instance, Always On off | P1v3 / PremiumV3, 2 instances, Always On on |
| SQL | `GP_S_Gen5` serverless + free-limit offer | `GP_Gen5` provisioned, 4 vCores, 128GB |
| Service Bus | Standard (cheapest tier with topic support — Basic has none) | Premium, 1 messaging unit |
| Static Web App | Free | Standard (custom domains, larger size limit) |

Service Bus can't get cheaper than Standard for either environment — Basic doesn't support topics/subscriptions at all, so Standard is already "the cheapest tier that exists" for this design; Premium is prod's genuine upgrade for dedicated capacity. **Premium has a fixed hourly cost regardless of traffic** (unlike Standard's consumption-ish pricing) — worth knowing before ever actually pointing `prod.bicepparam` at a real deployment.

## Secrets

**Superseded by day-25 for the JWT key specifically:** `jwtKey` is no longer a Bicep parameter at all — `day-25/README.md` covers moving it to a native Key Vault reference (`@Microsoft.KeyVault(SecretUri=...)`) resolved by the App Service's own managed identity at runtime, never passing through a template parameter. The rest of this section describes `sqlAdministratorPassword` and `serviceBusConnectionString`, which still work exactly as follows.

Two values are marked `@secure()` everywhere they appear (`main.bicep` and the modules that consume them) and have **no default**: `sqlAdministratorPassword`, `serviceBusConnectionString`. Neither, and no other credential, appears as a literal anywhere in this folder — confirmed by grepping the whole folder for password/secret/connection-string-shaped text and finding nothing (see Verify below).

Both `.bicepparam` files source these via `getSecret()`:

```bicep
param sqlAdministratorPassword = getSecret('<subscription-id>', 'rg-thinkschool-day17', 'kv-quoteshub-dev', 'sql-admin-password')
```

This compiles to a standard ARM Key Vault reference (`{"reference": {"keyVault": {"id": ...}, "secretName": "..."}}`) — confirmed by inspecting `az bicep build-params` output directly. **The vault itself is not created by this Bicep** — creating one was out of scope for a task whose hard constraint is "do not create any Azure resource," and a real one doesn't exist yet in this subscription. Before either `.bicepparam` file can deploy for real:

1. Create a Key Vault (`kv-quoteshub-dev` / `kv-quoteshub-prod`).
2. Populate it with secrets named `jwt-key`, `sql-admin-password`, `servicebus-connection-string`.
3. Grant whoever/whatever runs the deployment (a user, a service principal, a pipeline's managed identity) `Key Vault Secrets User` access.
4. Replace the `<subscription-id>` placeholder in the param file with the real subscription ID.

`staticWebAppRepositoryToken` is the one secure parameter left unassigned in both param files — it defaults to `''` in `main.bicep` and is genuinely only needed the moment a Static Web App is first created (to register the GitHub connection); both `swa-quotesui-thinkschool` (dev) and any future prod instance only need it once, not on every redeploy.

**Alternative for a pipeline instead of a human running `az deployment`:** skip `getSecret()` entirely, remove the assignment from the `.bicepparam` file, and inject the three values as CI secret variables via `--parameters jwtKey=$JWT_KEY ...` on the command line. This does *not* work by simply omitting them from the `.bicepparam` file while also referencing that file — Bicep's typed parameter files require every non-default parameter to be assigned in-file (confirmed empirically; see Verify) — so this alternative means not using `.bicepparam` for these three at all, passing classic inline `--parameters` instead.

## Verify

### `az bicep build` — clean, zero warnings

Ran on `main.bicep` and all four modules individually. All five compiled with no errors and no warnings — nothing suppressed, there was simply nothing to report.

### `az deployment group what-if` — both parameter files, against the real resource group

Run against `rg-thinkschool-day17` (the actual, existing resource group). One caveat, stated upfront: since no Key Vault exists yet, the committed `.bicepparam` files' `getSecret()` calls can't resolve for a real `what-if` run. I ran `what-if` against temporary, **uncommitted** copies with the three `getSecret()` calls swapped for an obviously-fake placeholder string (`'PLACEHOLDER-NOT-A-REAL-SECRET-FOR-WHATIF-ONLY'`) purely to exercise the templates end-to-end; those copies were deleted immediately after and are not part of this folder.

**Dev — change summary:**

| Resource | Change |
|---|---|
| `Microsoft.Web/serverfarms/asp-quotesapi-free` (App Service Plan) | **NoChange** |
| `Microsoft.Sql/servers/.../firewallRules/AllowAzureServices` | **NoChange** |
| `Microsoft.Sql/servers/sql-quotesapi-thinkschool` | Modify — adding tags only (`environment`, `project`); the server has none today |
| `Microsoft.Sql/servers/.../databases/quoteshub` | Modify — same tags-only reason |
| `Microsoft.Web/sites/quotesapi-thinkschool` | Modify — a few `siteConfig` defaults (`ftpsState`, `minTlsVersion`, `netFrameworkVersion`, `localMySqlEnabled`) not currently set explicitly, plus app settings I deliberately didn't supply real values for |
| `Microsoft.Web/staticSites/swa-quotesui-thinkschool` | Modify — see note below |
| Service Bus namespace, topic, both subscriptions | **Create** — expected, none of this exists yet |

The Static Web App's "Modify" includes some properties being reported as deletions (`deploymentAuthPolicy`, `provider`, `stableInboundIP`, `trafficSplitting`). These are server-computed, read-only fields Azure returns on a GET but that aren't valid to set in a template — a known `what-if` presentation quirk for this resource type, not something a real deployment would actually remove. I'm flagging this rather than either hiding it or asserting confidently that it's harmless without saying why I believe that.

**The thing this was actually meant to prove:** the App Service Plan matches exactly, and neither the Web App nor the Static Web App shows a **Delete** or **Create** action — both are in-place **Modify**, meaning the template correctly describes resources that already exist rather than trying to recreate them under the hood.

**Prod — change summary:**

Every prod-named resource (`asp-quotesapi-prod`, `quotesapi-thinkschool-prod`, `sql-quotesapi-prod`, `swa-quotesui-prod`, `sb-quoteshub-prod`, and its topic/subscriptions) shows **Create**, correctly, since none of them exist. Every dev-named resource shows **Ignore** — prod's parameter file doesn't reference the dev names at all, so it correctly leaves them alone. No accidental cross-contamination between environments.

One gap: the App Service module (plan + web app) doesn't appear in the prod change list at all. `what-if` reported why directly, as a diagnostic rather than an error:

> `NestedDeploymentShortCircuited` — a nested deployment got short-circuited... due to a nested template having a parameter that was not fully evaluated (e.g. contains a reference() function).

This is a documented Azure `what-if` limitation, not a template defect: `appService`'s parameters include `sql.outputs.serverFqdn` and `staticWebApp.outputs.defaultHostname`. In the **dev** run these resolve fine because that SQL server and Static Web App already exist (real, known values). In the **prod** run, both are being created fresh in the same deployment — `what-if`'s static preview can't compute a `reference()`-based output for a resource that doesn't exist yet, so it gives up validating the one module that depends on it rather than guessing. A real `az deployment group create` would not have this problem — Bicep's actual dependency ordering ensures `sql` and `staticWebApp` finish deploying before `appService` runs, at which point their outputs are real values. This is specifically a `what-if`-preview limitation, confirmed against Microsoft's own documented explanation (the diagnostic links directly to it), not something I'm asserting without a source.

### Secret grep — clean

```
grep -rniE "password\s*=\s*['\"][^'\"]+['\"]|secret\s*=\s*['\"][^'\"]+['\"]|connectionstring\s*=\s*['\"][^'\"]+['\"]|AccountKey=|SharedAccessKey=|BEGIN PRIVATE KEY|eyJ[A-Za-z0-9_-]{10,}" .
```
Zero matches. The only lines matching a looser `password=|connectionstring=` search are the three `getSecret()` calls (Key Vault references, not values) and `main.bicep`'s connection-string string-interpolation, which builds from the `sqlAdministratorPassword` *parameter reference* (`${sqlAdministratorPassword}`), never a literal.

## What I could not verify

- **Whether a real deployment of prod's App Service would succeed end-to-end** — blocked by the `what-if` limitation above, not tested via `az deployment group create` (which would cost money and violates the hard constraint regardless).
- **Whether the Static Web App's reported property deletions are truly inert** — I'm inferring this from the properties themselves being documented as read-only/computed, not from having run a real deployment and confirmed nothing changed.
- **Whether `getSecret()` against a real, populated Key Vault resolves cleanly** — no such vault exists in this subscription, so this was only validated up to the point of producing a correct ARM Key Vault reference object (via `az bicep build-params`), not an actual secret fetch.
- **The App Service Plan's `P1v3`/`PremiumV3` combination with a Linux/reserved plan** — `az bicep build` and the dev `what-if` don't exercise this combination (dev stays on F1), and prod's App Service module was the one short-circuited above, so this specific SKU pairing was never actually validated against the ARM API.

Nothing was deployed. `day-5/QuotesApi` and `day-13/quotes-ui` were not touched. Not committed.
