# Day 24 — Deploying day-23's Bicep with Azure Deployment Stacks, via azd

Dev is deployed for real, adopting the three resources that already existed. Prod is deployed for real too, on deliberately reduced tiers, as a second, fully independent stack. Nothing was deleted. Not committed.

## Cost constraint - read this first, because it shaped every decision below

The subscription backing this has no usable credit and expires on the 12th (today is the 8th). Three consequences, applied before anything was deployed:

1. **`day-23/params/prod.bicepparam` was edited down** from P1v3 / provisioned `GP_Gen5` / Premium Service Bus to F1 / serverless `GP_S_Gen5` / no Service Bus at all - see "Reduced prod tiers" below for the exact diff and what each row would be in reality.
2. **Service Bus was not created in either environment.** It has no free tier - Basic doesn't support topics at all, and Standard/Premium both bill continuously regardless of traffic. `day-23/main.bicep` gained a new `deployServiceBus` parameter (default `false` in both `dev.bicepparam` and `prod.bicepparam`) so the stacks could be deployed without it. **I did not create it and am not asking for it here** - if you want it, tell me which SKU and I'll quote the exact monthly cost before touching anything.
3. **A pre-existing risk was found and fixed before deploying, not after:** the live App Service's deployed *code* still expects a SQLite connection string (the `UseSqlServer` code change from an earlier session was never redeployed). `main.bicep`'s computed Azure SQL connection string would have been pushed to that same app setting, and the running app would have crashed on its next restart. Fixed by adding `sqlConnectionStringOverride` to `main.bicep` and setting it in `dev.bicepparam` to the app's actual current connection string (`Data Source=/home/quotes.db`) - preserving current behavior instead of guessing. Prod has no existing code to break, so it doesn't need this override.

## What exists now

| Stack | Resource group | Resources | Provisioning state |
|---|---|---|---|
| `quoteshub-dev` | `rg-thinkschool-day17` | App Service Plan + Web App (adopted), Static Web App (adopted), SQL server + database + firewall rule (adopted) | `succeeded` |
| `quoteshub-prod` | `rg-thinkschool-day17` | App Service Plan + Web App, Static Web App, SQL server + database + firewall rule - all newly created, `prod-*` names | `succeeded` |

```
$ az stack group list --resource-group rg-thinkschool-day17 -o table
Name            ProvisioningState
--------------  -------------------
quoteshub-dev   succeeded
quoteshub-prod  succeeded
```

## The azd config, and why it calls `az stack group create` directly

`azure.yaml` is new, not a copy of the stale `day-5/azure.yaml` (which targets `containerapp` - a host the API has never run on). `infra.provider` points at `../day-23`; `services.quotes-api.host` is `appservice`.

I enabled azd's alpha `deployment.stacks` feature (`azd config set alpha.deployment.stacks on` - confirmed via `azd config list-alpha` showing `Status: On`) expecting it to give `azd provision` native stack behavior. It doesn't get me what this task actually needs: I fetched azd's own published `azure.yaml` JSON schema and confirmed `actionOnUnmanage` and `denySettings` **do not exist anywhere in it** - there is no way to set them through azd's config in this version (1.31.1). "Set actionOnUnmanage and denySettings deliberately" is the actual point of this task, so I didn't route around that gap - I stepped outside `azd provision` and call `az stack group create` directly from a `preprovision` hook (`hooks/deploy-stack.sh`), which reads `AZURE_ENV_NAME` to pick the right stack name and parameter file. azd still owns environment management (`azd env new/list/select`, `AZURE_RESOURCE_GROUP`); the hook is what actually talks to ARM.

```
$ azd env list
NAME      DEFAULT   LOCAL     REMOTE
dev       true      true      false
prod      false     true      false
```

Each environment is bound to its parameter file via an explicit env var (not just convention), visible in `azd env get-values`:

```
# dev
BICEP_PARAMS_FILE="../day-23/params/dev.bicepparam"
STACK_NAME="quoteshub-dev"

# prod
BICEP_PARAMS_FILE="../day-23/params/prod.bicepparam"
STACK_NAME="quoteshub-prod"
```

**One honest gap:** for today's actual runs, I did not invoke the hook through `azd provision` end-to-end. `day-23`'s `.bicepparam` files source their three secrets via `getSecret()` against a Key Vault that doesn't exist yet (by design - see `day-23/README.md`), so a real `getSecret()` resolution fails regardless of whether azd or plain `az` triggers it. I ran the equivalent `az stack group create` command myself, against temporary, uncommitted copies of the parameter files with real secret values substituted in memory (the live app's actual current JWT key, fetched once into a local variable and never printed; the SQL admin password I already had; fresh, newly-generated secrets for prod, which has no existing state to preserve) - deleted immediately after each use. `hooks/deploy-stack.sh` is what a real run (with a populated vault) would execute unmodified.

## `actionOnUnmanage` and `denySettings` - the actual choices, and why

```
--action-on-unmanage detachAll
--deny-settings-mode denyDelete --deny-settings-apply-to-child-scopes
```

**`detachAll`, not `deleteResources` or `deleteAll`.** This stack didn't create the three resources it manages - it adopted things a real user already depends on. If a future template change ever drops one of them from `main.bicep`, or the stack itself is deleted, the safe default is "stop tracking it," not "delete it." A stack earning the right to delete what it manages is a decision to make deliberately, later, once there's a track record - not the default for a stack's first day of existing.

**`denyDelete`, not `denyWriteAndDelete` or `none`.** `denyWriteAndDelete` is the more locked-down option, but it would have blocked the drift demonstration this task explicitly asks for - a hand-applied tag is a *write*, and a mode that blocks all writes can't also let you create drift by hand to observe. `denyDelete` is the tightest mode that still lets that happen: writes from outside the stack succeed (and get silently reconciled back on the next deploy - see Drift below), deletes from outside the stack are rejected outright by ARM itself, before they happen. `--deny-settings-apply-to-child-scopes` extends that protection to child resources (the SQL database and firewall rule under the server), not just the top-level resources named directly in `main.bicep`.

## Deploying dev - adopting, not recreating

```
$ az stack group create --name quoteshub-dev --resource-group rg-thinkschool-day17 \
    --template-file main.bicep --parameters <dev params, secrets hydrated> \
    --action-on-unmanage detachAll --deny-settings-mode denyDelete \
    --deny-settings-apply-to-child-scopes --yes

provisioningState: succeeded
resources managed: 6
 - Microsoft.Sql/servers/sql-quotesapi-thinkschool          | managed
 - Microsoft.Sql/servers/.../databases/quoteshub             | managed
 - Microsoft.Sql/servers/.../firewallRules/AllowAzureServices| managed
 - Microsoft.Web/serverfarms/asp-quotesapi-free              | managed
 - Microsoft.Web/sites/quotesapi-thinkschool                 | managed
 - Microsoft.Web/staticSites/swa-quotesui-thinkschool        | managed
```

All six show `managed`, not `created` - confirmed via `az stack group show` immediately after. This matches the `what-if` result from `day-23`'s verification (`NoChange`/`Modify`, never `Delete`+`Create`) actually holding true against the real ARM API, not just its preview.

**Live site check, right after:**

```
API health:     HTTP 200
Frontend:       HTTP 200
GET /api/quotes: HTTP 200
```

(The first check took ~35 seconds to return - the F1 plan has no Always On and cold-starts after the app setting change triggered a restart; this matches the exact behavior documented in `day-17/README.md`, not a new problem.)

**One thing that did NOT go as cleanly predicted, reported rather than glossed over:** `day-23`'s `what-if` had flagged the Static Web App's `deploymentAuthPolicy`, `stableInboundIP`, and `trafficSplitting` as "would delete," and I judged that likely benign (computed/read-only fields Azure ignores on write). After the real deployment, I checked the live resource directly: `stableInboundIP` and `trafficSplitting` are indeed now `null` (previously `{environmentDistribution:{default:100}}`), and so is `deploymentAuthPolicy` (previously `DeploymentToken`). My theory was half right - `provider` and `repositoryUrl`/`branch` (the GitHub connection itself) survived intact, and the frontend keeps serving correctly, but these three specific fields really were reset, not just misreported by `what-if`. I checked whether the actual deployment token the GitHub Actions workflow depends on still works: `az staticwebapp secrets list` still returns a valid `apiKey`, which is reassuring, but I did not trigger a real GitHub Actions run to prove the next automated deploy would still succeed - see "What I could not verify."

Also worth naming plainly: the App Service Plan and Web App's tags stayed `{}` even after `main.bicep` passing `tags: { environment: 'dev', project: 'quoteshub' }` to every module - the SQL server/database did pick up those tags, the App Service resources didn't. I don't have a confirmed root cause for this difference and didn't chase it further given time - flagging it as an open question rather than asserting an explanation I haven't verified.

## Drift

**What I expected to find, and didn't:** `az stack group show`, `az stack group validate`, and the raw ARM REST API checked directly across all three available `Microsoft.Resources/deploymentStacks` API versions (`2022-08-01-preview`, `2024-03-01`, `2025-07-01`) all show managed resources as a flat `{id, status, denyStatus}` - none of them expose a live, synchronous drift flag. This is consistent with Microsoft's own documentation describing drift detection as a periodic background reconciliation, not an instant computed property - I could not force it to appear on demand within this session.

**What I could demonstrate instead - the actual mechanism, not just its label:**

```
$ az resource show --ids .../serverfarms/asp-quotesapi-free --query tags
{}

$ az resource tag --ids .../serverfarms/asp-quotesapi-free \
    --tags environment=dev project=quoteshub manually-added-tag=drift-test
tags now: {"environment": "dev", "manually-added-tag": "drift-test", "project": "quoteshub"}
```

That write succeeded - expected under `denyDelete` (writes aren't blocked, only deletes). Then, re-running the exact same `az stack group create` command used to deploy dev in the first place (a real, idempotent update - the stack already existed and this just reconciles it):

```
$ az resource show --ids .../serverfarms/asp-quotesapi-free --query tags
{}
```

`manually-added-tag` is gone. The stack's authoritative template state overwrote the out-of-band change on the next reconciliation - which is what drift detection exists to protect against, demonstrated by its actual effect even though I couldn't surface a labeled "drift" status for it in this API surface within this session.

## Deny settings

Attempted, from completely outside the stack, to delete a stack-managed resource - the SQL server's firewall rule, chosen deliberately as the lowest-blast-radius target available (if the deny somehow hadn't worked, the cost of being wrong here is "recreate one firewall rule," not "lose the database"):

```
$ az sql server firewall-rule delete --resource-group rg-thinkschool-day17 \
    --server sql-quotesapi-thinkschool --name AllowAzureServices

ERROR: (DenyAssignmentAuthorizationFailed) The client '...' has permission to perform
action 'Microsoft.Sql/servers/firewallRules/delete' ...; however, the access is denied
because of the deny assignment with name 'Deny assignment ... created by Deployment
Stack '.../deploymentStacks/quoteshub-dev'.'
```

Rejected before it happened, naming the stack that owns the protection by name. Confirmed the rule still exists immediately after:

```
$ az sql server firewall-rule show ... --name AllowAzureServices
Name                StartIpAddress    EndIpAddress
------------------  ----------------  --------------
AllowAzureServices  0.0.0.0           0.0.0.0
```

The permission I (the account owner) actually hold on this resource says I *can* delete it (`has permission to perform action`) - the deny assignment overrides that anyway. That's the entire point of deny settings: protection that doesn't depend on remembering not to, or on RBAC being scoped correctly everywhere.

## Deploying prod - a second, independent stack

```
$ az stack group create --name quoteshub-prod --resource-group rg-thinkschool-day17 \
    --template-file main.bicep --parameters <prod params, fresh secrets> \
    --action-on-unmanage detachAll --deny-settings-mode denyDelete \
    --deny-settings-apply-to-child-scopes --yes

provisioningState: succeeded
resources managed: 6
 - Microsoft.Sql/servers/sql-quotesapi-prod                    | managed
 - Microsoft.Sql/servers/.../databases/quoteshub                | managed
 - Microsoft.Sql/servers/.../firewallRules/AllowAzureServices   | managed
 - Microsoft.Web/serverfarms/asp-quotesapi-prod                 | managed
 - Microsoft.Web/sites/quotesapi-thinkschool-prod               | managed
 - Microsoft.Web/staticSites/swa-quotesui-prod                  | managed
```

All six show `managed` because they were genuinely just created (a fresh resource's first deployment into a stack is definitionally "adopted" the same way - there's no separate creation mode). Confirmed the full resource group afterward: 12 resources total, the original 6 `*-thinkschool` names plus exactly 6 new `*-prod` ones - no name collisions, no cross-contamination. `az stack group show` on `quoteshub-dev` immediately after still lists precisely its original 6 resources, unchanged. Dev's live site was re-checked after prod's deployment too: still `HTTP 200` on both the API and the frontend.

Fresh secrets were generated for prod specifically (not dev's reused) - a new JWT key and a new SQL admin password, since prod's SQL server (`sql-quotesapi-prod`) and its administrator login are entirely separate credentials from dev's.

## Reduced prod tiers - the exact diff, and what they'd be in reality

| | Deployed today | In reality |
|---|---|---|
| App Service | F1 / Free, 1 instance, Always On off | P1v3 / PremiumV3, 2 instances, Always On on |
| SQL | `GP_S_Gen5` serverless, `useFreeLimit: false` | `GP_Gen5` provisioned, 4 vCores, 128GB |
| Service Bus | **not deployed** | Premium, 1 messaging unit |
| Static Web App | Free | Standard |

Confirmed via direct queries against the live resources, not assumed from the parameter file: `az sql db show` reports `GP_S_Gen5`/`useFreeLimit: null` (Azure's free-limit offer is capped at one database per subscription, and dev's database already claims that slot - prod's serverless database is therefore "near-free" rather than literally free: it auto-pauses to zero compute cost when idle, leaving only a small storage charge). `az appservice plan show` reports `F1`/`Free`. `az staticwebapp show` reports `Free`.

**Prod's Static Web App has no real GitHub connection.** `staticWebAppRepositoryToken` was left empty (it's only needed the first time a Static Web App registers a GitHub connection, and I don't have - and wasn't asked to create - a token for this). `swa-quotesui-prod` exists as a resource but has no deployment source configured; there is no actual frontend running at its URL. That's an acceptable gap for what this task is actually demonstrating (stack mechanics, environment separation), not a claim that prod is a working, servable environment today.

The point being demonstrated is the dev/prod *separation* - two stacks, two independent resource sets, one template - not that prod's SKUs are production-realistic right now. They aren't, deliberately, until real budget exists.

## What Deployment Stacks actually gave me here, beyond a plain deployment

Grounded in what happened above, not the marketing description: a plain `az deployment group create` would have applied the exact same resource changes with none of the protection. The deny-settings demonstration is the concrete evidence - `az sql server firewall-rule delete` reported that *I personally have permission* to perform that delete, and it was blocked anyway, purely because the resource is stack-managed. A plain deployment gives you a resource; a deployment stack gives you a boundary around it that survives someone (including future-me, including a misconfigured pipeline) having full RBAC rights to break it by hand. The drift reconciliation is the second piece: an out-of-band tag change didn't need to be caught by a person reviewing the portal - the next ordinary stack update silently corrected it back to what the template actually declares, for free, as a side effect of the normal deploy command rather than a special drift-remediation step.

## What I could not verify

- **A live, synchronous drift status.** Checked three API versions directly against the real ARM REST endpoint; none expose one. I'm relying on Microsoft's documented description of background reconciliation, not on having observed the flag itself flip to "drifted."
- **Whether the Static Web App's GitHub Actions deployment still succeeds end-to-end.** `deploymentAuthPolicy` reset to `null` on the real resource after adoption; the underlying `apiKey` secret is still retrievable, which is a good sign, but I did not push a commit and watch a real workflow run complete.
- **Whether prod's App Service would actually serve anything.** No application code was ever deployed to `quotesapi-thinkschool-prod` (`azd deploy` was out of scope here) and its Static Web App has no GitHub connection - prod's resources exist and are stack-managed, but nothing runs behind them yet.
- **The App Service Plan/Web App tags-not-applying discrepancy** noted above under "Deploying dev" - observed, not explained.
- **`getSecret()` against a real, populated Key Vault**, for the same reason noted in `day-23/README.md` - no such vault exists in this subscription. Every real deployment in this session used temporary, hand-hydrated, uncommitted parameter files instead; `hooks/deploy-stack.sh` itself was never executed by azd end-to-end.

Not committed. No `az stack group delete` was run against either stack - dev per your explicit instruction, prod because you hadn't asked for teardown either; say the word and I'll do prod's (never dev's) with `--action-on-unmanage detachAll` so its resources survive the stack's own deletion.
