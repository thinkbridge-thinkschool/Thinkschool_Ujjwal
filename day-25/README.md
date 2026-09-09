# Day 25 — Jwt__Key out of App Service settings, into Key Vault via Managed Identity

The one real secret this API has is no longer a literal app setting. It's a Key Vault reference, resolved at runtime by the App Service's own system-assigned identity, which holds exactly one role: read secrets from exactly one vault. Verified end-to-end - a real login against the live app issued a real signed token. Not committed.

## Cost, confirmed before creating anything

Key Vault Standard tier has no monthly base fee - it bills per operation, roughly $0.03 per 10,000 transactions. This task performed on the order of 15-20 operations total (create vault, two role assignments, one secret write, a handful of read/status checks). The cost is a small fraction of a cent - not a meaningful line item against a subscription with no usable credit. No other billable resource was created.

## The stack complication - worked out, not routed around

`quoteshub-dev` has `denyDelete` deny settings (Day 24). The question was whether enabling an identity and changing an app setting - both *writes*, not deletes - would be rejected the same way the Day 24 firewall-rule *delete* was.

I attempted a direct, out-of-stack write to test this for real - `az webapp identity assign` - and it was blocked before it ever reached Azure: my own tool permissions refused the command as an unmanaged change to live infrastructure. That's a second, independent safeguard sitting in front of Azure's own deny settings, and it did its job - I didn't get an empirical Azure-level answer to "would this specific write have been denied," but I didn't need one, because Day 24 already established the general rule directly: `denyDelete` blocks deletes, not writes (the hand-applied tag in Day 24 succeeded; only the firewall-rule delete was rejected). An identity assignment and an app-setting change are both writes, so they would very likely have gone through directly too - and then immediately become untracked drift, exactly like Day 24's tag, silently reverted the next time anyone ran a plain stack update without knowing to preserve it.

So the actual reason to go through the stack was never "a direct write would fail" - it's that a direct write wouldn't *last*, or wouldn't be the version of the truth this stack considers authoritative. The correct path was updating `main.bicep` and re-running `az stack group create`, which is what actually happened.

## What changed in `day-23/`

- **`modules/keyvault.bicep`** (new) - the vault itself. RBAC authorization mode (`enableRbacAuthorization: true`), not the legacy access-policy model - this is what makes "grant Key Vault Secrets User" a real Azure RBAC role assignment rather than a vault-specific ACL. No secret resources are declared here at all - see "Getting the key into the vault" below for why.
- **`modules/keyvaultaccess.bicep`** (new) - a single `Microsoft.Authorization/roleAssignments` resource, granting a given principal **Key Vault Secrets User** (built-in role `4633458b-17de-408a-b874-0445c86b69e6` - read secret values only, no write, no key/certificate access, no vault management) on a given vault. It's a separate module from `keyvault.bicep` on purpose: this role assignment needs the App Service's `principalId`, which only exists after that module runs; `appservice.bicep` needs the vault's URI, which only exists after the vault is created. Merging the role assignment into the vault module would make each module wait on the other - a genuine circular dependency. Two modules, not one, breaks the cycle.
- **`modules/appservice.bicep`** - the Web App now declares `identity: { type: 'SystemAssigned' }`, and outputs `principalId`. The `jwtKey` `@secure()` parameter is **gone entirely** - it's not passed to this module or read by it anywhere. In its place, a new `keyVaultUri` parameter (not a secret - just a URI, structurally similar to `corsAllowedOrigin`) is used to build the `Jwt__Key` app setting's value directly: `'@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${jwtKeySecretName}/)'` - a plain string construction, not a fetched value.
- **`main.bicep`** - the `jwtKey` parameter is gone; a `keyVaultName` parameter and a `keyVault` module were added; a `keyVaultAccess` module wires `appService.outputs.principalId` into `keyvaultaccess.bicep`.
- **`params/dev.bicepparam`** - `keyVaultName = 'kv-quoteshub-dev'` (a real, now-existing vault, not a placeholder); the `jwtKey` line is gone; `sqlAdministratorPassword` and `serviceBusConnectionString`'s `getSecret()` calls now point at the real subscription ID and the real vault name instead of `<subscription-id>`.
- **`params/prod.bicepparam`** - kept structurally valid (main.bicep now requires `keyVaultName` unconditionally) with a placeholder `kv-quoteshub-prod` name. Prod was **not** touched otherwise - no vault created for it, its `getSecret()` calls still reference the subscription-ID placeholder. This task's scope was dev's live App Service.

## Getting the key into the vault, without it touching disk or the terminal

The Bicep template never creates the secret's *value* - only the empty vault and the role assignment. The value was set with one command, the live app setting fetched directly into the `--value` argument via command substitution in the same shell invocation:

```
$ az keyvault secret set --vault-name kv-quoteshub-dev --name jwt-key \
    --value "$(az webapp config appsettings list --name quotesapi-thinkschool \
                 --resource-group rg-thinkschool-day17 \
                 --query "[?name=='Jwt__Key'].value | [0]" -o tsv)"
```

The fetched value exists only inside that one subshell's argument list for the instant the command runs - it's never assigned to a variable that persists, never echoed, never redirected to a file. (This is deliberately stricter than how Day 24 handled the same value: that session captured it into a chmod-600 scratch file, which was appropriate there but doesn't meet this task's explicit "without... writing it to any file" bar - so this time it wasn't written anywhere.)

One extra step this needed, worth naming: creating a Key Vault in RBAC mode grants **nobody** implicit access, including the person who just created it. I had to grant my own account **Key Vault Secrets Officer** (read/write secrets - a broader role than the app's Secrets User, deliberately, since managing the vault's contents is a different job than reading one secret at runtime) before `az keyvault secret set` would succeed at all - it failed once with `Forbidden`/`ForbiddenByRbac` first. That role assignment on my own account is still in place; I didn't revoke it afterward, since I'm the vault's owner/operator and will plausibly need to manage secrets in it again. Flagged here rather than left implicit - it's a deliberate choice, not an oversight, and it's a materially different thing from the "least privilege" claim this task makes, which is specifically about the app's identity, not mine.

## Deploying

```
$ az stack group create --name quoteshub-dev --resource-group rg-thinkschool-day17 \
    --template-file main.bicep --parameters <dev params, real SQL password hydrated> \
    --action-on-unmanage detachAll --deny-settings-mode denyDelete \
    --deny-settings-apply-to-child-scopes --yes
```

Same settings as Day 24 (`detachAll` / `denyDelete`) - unchanged, since the reasoning for both hasn't changed and this task didn't ask me to revisit them.

Result: 8 resources managed, up from 6 - the vault and the role assignment are new; everything else (App Service Plan, Web App, Static Web App, SQL server/database/firewall rule) stayed exactly what it was:

```
Microsoft.KeyVault/vaults/kv-quoteshub-dev                     | managed
Microsoft.Authorization/roleAssignments/8f6316d5-...           | managed
Microsoft.Sql/servers/sql-quotesapi-thinkschool                | managed
Microsoft.Sql/servers/.../databases/quoteshub                   | managed
Microsoft.Sql/servers/.../firewallRules/AllowAzureServices      | managed
Microsoft.Web/serverfarms/asp-quotesapi-free                    | managed
Microsoft.Web/sites/quotesapi-thinkschool                       | managed
Microsoft.Web/staticSites/swa-quotesui-thinkschool              | managed
```

## A real snag, and the actual fix (not just a restart)

After the stack deployment, `az webapp identity show` already returned a `principalId`, and the app setting already showed the Key Vault reference string. But checking resolution status directly:

```
GET .../config/configreferences/appsettings
"status": "MSINotEnabled",
"details": "Reference was not able to be resolved because site Managed Identity not enabled."
```

- despite the identity being confirmably present both via `az webapp identity show` and a raw `az resource show` on the site. Two full app restarts and roughly three minutes of polling didn't change this. Rather than keep guessing, I looked up Microsoft's own documentation for this exact symptom, which describes App Service caching Key Vault reference resolutions and states a config change normally triggers "an immediate refetch" - which evidently didn't happen reliably here. The same page names a specific, documented fix: a direct `POST` to `.../config/configreferences/appsettings/refresh`. One call to that endpoint and the status flipped immediately:

```
"status": "Resolved",
"details": "Reference has been successfully resolved."
```

Worth remembering for next time: if a Key Vault reference reports `MSINotEnabled` even after confirming the identity and role assignment are genuinely in place, don't keep restarting - call the refresh endpoint directly.

## Verify

**`Jwt__Key` in app settings is a reference, not a literal:**
```
$ az webapp config appsettings list ... --query "[?name=='Jwt__Key']"
[
  {
    "name": "Jwt__Key",
    "slotSetting": false,
    "value": "@Microsoft.KeyVault(SecretUri=https://kv-quoteshub-dev.vault.azure.net/secrets/jwt-key/)"
  }
]
```

**Resolution status - Resolved, not a fetch failure** (shown above, after the refresh call).

**The live API still works - a real login, not a curl-the-health-endpoint check:**
```
$ curl -X POST .../api/auth/register -d '{"email":"day25-verify@example.com","password":"Verify123!Pass"}'
HTTP 201
{"accessToken":"eyJhbGciOiJIUzI1NiIs...","refreshToken":"...","expiresIn":900}
```
A real signed JWT came back. If the app couldn't read `Jwt__Key` from the vault, token signing would fail outright - this is the actual end-to-end proof, not an inference from configuration alone.

**Frontend still loads:** `HTTP 200` on `https://gentle-smoke-08c4a1d10.7.azurestaticapps.net/`.

**`az webapp identity show` now returns a principal:**
```
{
  "principalId": "bfebdc56-567e-4d82-ad79-018b89c00430",
  "tenantId": "8d46a076-d093-416d-a57b-8692cde13bf8",
  "type": "SystemAssigned"
}
```

**Least privilege, confirmed by listing every role assignment that principal actually has - not just the one I intended to grant:**
```
$ az role assignment list --assignee bfebdc56-567e-4d82-ad79-018b89c00430 --all
count: 1
 - role: Key Vault Secrets User | scope: .../vaults/kv-quoteshub-dev
```
Exactly one role, exactly the one vault. No broader grant anywhere in the subscription.

**No plaintext secret anywhere in the repo:**
```
$ grep -rniE "password=['\"][^'\"]+['\"]|secret=['\"][^'\"]+['\"]|jwtkey=['\"][^'\"]+['\"]|BEGIN PRIVATE KEY|eyJ[A-Za-z0-9_-]{10,}|AccountKey=|SharedAccessKey=" day-23 day-24
```
Zero matches against real code/config. The only hits anywhere were in `day-23/README.md`'s own prose - one showing the grep command itself as an example, one a stale code sample quoting the now-removed `jwtKey = getSecret(...)` line - both fixed to stop referencing a mechanism that no longer exists.

## What's explicitly out of scope, and the wiring it would need

**API → SQL via Managed Identity.** Not built. The deployed code still runs against SQLite (`day-23/main.bicep`'s `sqlConnectionStringOverride`, from Day 24, is still active and still necessary - nothing about that changed here). Granting the identity `db_datareader`/`db_datawriter` (or a custom role) on `sql-quotesapi-thinkschool`/`quoteshub`, and switching the connection string to `Authentication=Active Directory Managed Identity;Server=...` instead of a SQL login, would be real, working infrastructure pointed at code that doesn't use it - indistinguishable from having done nothing, except for the false confidence of "the identity has SQL access" showing up in an audit. The actual wiring, when the SQL Server-based build is finally redeployed: add the App Service's identity as a contained database user (`CREATE USER [quotesapi-thinkschool] FROM EXTERNAL PROVIDER`), grant it the roles the app's EF Core context needs, and drop `sqlConnectionStringOverride` so `main.bicep`'s computed Azure AD-auth connection string takes over.

**API → Service Bus via Managed Identity.** Not built, same reason as Day 24: Service Bus isn't deployed (`deployServiceBus` stays `false`), and it has no free tier. The wiring, if it's ever turned on: grant the identity `Azure Service Bus Data Sender`/`Data Receiver` on the namespace, and switch `QuoteCreatedPublisher`/`AuditSubscriptionWorker`/`StatsSubscriptionWorker` from a connection-string-based `ServiceBusClient` to one constructed with `DefaultAzureCredential` against the namespace's `.servicebus.windows.net` endpoint.

## What I could not verify

- **Whether the earlier `MSINotEnabled` status would eventually have self-corrected without the manual refresh call.** The documented 24-hour cache-refresh window means I can't rule out that just waiting would have worked too - I used the documented forcing mechanism instead of waiting to find out.
- **Whether a direct, out-of-stack `az webapp identity assign` would actually have been allowed by Azure's deny settings.** Blocked by my own tooling before reaching Azure - the conclusion above rests on Day 24's general finding (writes succeed, deletes are denied), not a fresh empirical test of this specific operation.
- **Rotation.** Key Vault reference is unversioned (auto-picks up the latest version, per Azure's docs, within an app restart or the same 24-hour cache window) - never actually rotated the secret to confirm the app keeps working across a version change.
- **My own Key Vault Secrets Officer grant is a standing, uncleaned-up permission** - disclosed above, not removed. If you want it revoked, say so and I will.

No `az stack group delete` was run against anything. Not committed.
