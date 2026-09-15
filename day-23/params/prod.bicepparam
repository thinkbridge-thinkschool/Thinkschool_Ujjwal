using '../main.bicep'

// eastasia, not centralus - day-27 rebuilt on a new "Azure for Students"
// subscription whose policy only allows a specific region set, and of
// those, only eastasia also supports Static Web Apps. See
// day-27/README.md for the full reasoning; prod uses the same region as
// dev for the same reason, not a separate decision.
param location = 'eastasia'
param environmentName = 'prod'

// --- App Service - REDUCED FOR COST (day-24): the subscription backing
// this deployment has no usable credit and expires on the 12th, so this
// is F1/Free, same as dev, rather than the P1v3/PremiumV3 a real
// production environment would actually run. The prod-* naming and the
// fact that this is a wholly separate stack from dev is the thing being
// demonstrated here, not the SKU size. In reality this row would be:
//   appServiceSkuName = 'P1v3', appServiceSkuTier = 'PremiumV3',
//   appServiceSkuCapacity = 2, appServiceAlwaysOn = true
// (supports Always On, autoscale, staging slots - none of which F1 allows).
param appServicePlanName = 'asp-quotesapi-prod'
param webAppName = 'quotesapi-thinkschool-prod2'
param appServiceSkuName = 'F1'
param appServiceSkuTier = 'Free'
param appServiceSkuCapacity = 1
param appServiceAlwaysOn = false // F1 does not support Always On
param dotnetVersion = '10.0'
param aspnetCoreEnvironment = 'Production'
param entraAudience = '9595ac6d-99d5-42b5-bffe-fed74bce6f42'
param enableServiceBusIntegration = false // Service Bus is not being deployed at all right now - see below

// --- Azure SQL - day-28: prod now SHARES dev's actual database instead
// of getting its own. deploySql = false means main.bicep never runs
// sql.bicep for this deployment at all - sqlServerName/sqlDatabaseName
// below name dev's real, already-deployed server/database, and
// sqlAdministratorPassword (further down) is dev's real admin password,
// not a new one. This is the deliberate trade for staying at $0/month
// on a $100 one-time student credit: the free-limit offer covers only
// one database per subscription, and a second, non-free database - even
// "near-free" serverless - is a real, ongoing, avoidable cost for a
// capstone project with no actual production traffic. The accepted
// consequence: prod and dev now share the same data AND the same SQL
// credentials, not just the same server. A real production system would
// never do this - it is a cost trade for a learning environment, stated
// here rather than left implicit. The sku*/maxSizeBytes/useFreeLimit
// params below are inert while deploySql is false (sql.bicep never
// runs) - kept only so this file stays structurally valid and documents
// what a real, separate prod database would have used.
param deploySql = false
param sqlServerName = 'sql-quotesapi-thinkschool2' // dev's real server - see deploySql comment above
param sqlAdministratorLogin = 'quoteshubadmin'
param sqlDatabaseName = 'quoteshub' // dev's real, shared database
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuFamily = 'Gen5'
param sqlSkuCapacity = 2
param sqlMaxSizeBytes = 34359738368 // 32 GB
param sqlUseFreeLimit = false

// --- Service Bus - NOT DEPLOYED. There is no free tier for any SKU that
// supports topics (Basic doesn't support them at all; Standard and
// Premium both bill continuously). Deploying this for a demo would be a
// real, avoidable monthly cost - deployServiceBus stays false in both
// dev and prod until that cost is explicitly asked for and accepted.
// These SKU values are kept only to document what prod would use if it
// were ever turned on - none of them are provisioned right now.
param serviceBusNamespaceName = 'sb-quoteshub-prod'
param serviceBusSkuName = 'Premium'
param serviceBusPremiumMessagingUnits = 1
param serviceBusTopicName = 'quote-created'
param deployServiceBus = false

// --- Static Web App - REDUCED FOR COST (day-24): Free tier, same as
// dev, instead of Standard. In reality this would be:
//   staticWebAppSkuTier = 'Standard'
// (custom domains with managed certificates, larger app/API size limit).
param staticWebAppName = 'swa-quotesui-prod2'
param staticWebAppSkuTier = 'Free'
param staticWebAppRepositoryUrl = 'https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal'
param staticWebAppBranch = 'main'

// --- Key Vault - day-28: prod gets its OWN vault, created fresh in
// rg-thinkschool-prod, holding its OWN jwt-key (a different signing key
// than dev's - sharing the database doesn't require sharing the token
// signing key, and keeping it separate means a token minted by one
// environment doesn't validate against the other, even though both
// read/write the same Users table). ---
param keyVaultName = 'kv-quoteshub-prod'

// --- Application Insights - day-28: created fresh for prod, separate
// from dev's, so telemetry from the two environments doesn't mix -
// workspace-based, free ingestion tier, same as dev. ---
param logAnalyticsWorkspaceName = 'law-quoteshub-prod'
param appInsightsName = 'appi-quoteshub-prod'
param logAnalyticsRetentionInDays = 30

// --- Secrets: never literals, never defaults. sqlAdministratorPassword
// is the one deliberate cross-environment reference in this file - it
// reads dev's REAL vault (kv-quoteshub-dev2, rg-thinkschool-day17),
// because deploySql = false above means this deployment authenticates
// to dev's actual database using dev's actual credentials, not new ones
// of its own. serviceBusConnectionString stays pointed at prod's own
// (not-yet-existing) vault - it's an unused placeholder either way,
// since deployServiceBus is false. ---
param sqlAdministratorPassword = getSecret('e55c32ce-f67a-4e77-b258-3a6dc2822724', 'rg-thinkschool-day17', 'kv-quoteshub-dev2', 'sql-admin-password')
param serviceBusConnectionString = getSecret('e55c32ce-f67a-4e77-b258-3a6dc2822724', 'rg-thinkschool-prod', 'kv-quoteshub-prod', 'servicebus-connection-string')

// staticWebAppRepositoryToken is left unassigned: it defaults to '' in
// main.bicep. Only required the first time swa-quotesui-prod is ever
// created - supply it once, out-of-band, on that single deployment.
