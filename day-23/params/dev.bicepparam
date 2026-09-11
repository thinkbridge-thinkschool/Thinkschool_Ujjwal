using '../main.bicep'

// Changed from centralus (Day 17-26's region): the new "Azure for
// Students" subscription's built-in region-restriction policy
// (sys.regionrestriction) only allows centralindia, eastasia, uaenorth,
// indiasouthcentral, malaysiawest - and of those, only eastasia also
// supports Static Web Apps (which is only available in Central US, East
// US 2, West US 2, West Europe, East Asia). eastasia is the one region
// that satisfies both constraints; confirmed App Service, SQL, Key
// Vault, and Log Analytics all support it too before choosing it.
param location = 'eastasia'
param environmentName = 'dev'

// --- App Service - F1 is the cheapest tier that exists. This matches
// what is actually deployed today (rg-thinkschool-day17). ---
param appServicePlanName = 'asp-quotesapi-free'
param webAppName = 'quotesapi-thinkschool2'
param appServiceSkuName = 'F1'
param appServiceSkuTier = 'Free'
param appServiceSkuCapacity = 1
param appServiceAlwaysOn = false // F1 does not support Always On
param dotnetVersion = '10.0'
param aspnetCoreEnvironment = 'Production' // matches the app setting actually configured today
param entraAudience = '9595ac6d-99d5-42b5-bffe-fed74bce6f42' // public - see InfrastructureExtensions.cs comment
param enableServiceBusIntegration = false // reverted for the day-27 subscription rebuild - the day-26 attempt to deploy real Service Bus never reached Azure (interrupted by local disk space) and this task requires asking again before deploying anything with a real cost

// --- Azure SQL - GP_S_Gen5 serverless with the free-limit offer is the
// cheapest configuration that exists. Matches the database actually
// created in rg-thinkschool-day17 this session. ---
param sqlServerName = 'sql-quotesapi-thinkschool2'
param sqlAdministratorLogin = 'quoteshubadmin'
param sqlDatabaseName = 'quoteshub'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuFamily = 'Gen5'
param sqlSkuCapacity = 2
param sqlMaxSizeBytes = 34359738368 // 32 GB
param sqlUseFreeLimit = true

// The App Service's deployed CODE still expects its old SQLite
// connection string - the UseSqlServer code change was made locally but
// never redeployed (day-24 is about adopting resources into a stack,
// not about finishing that cutover). Pushing the computed Azure SQL
// connection string here instead would crash the live app the moment
// it restarts after this app setting changes. Remove this override once
// the SQL Server-based build is actually redeployed to this Web App.
param sqlConnectionStringOverride = 'Data Source=/home/quotes.db'

// --- Service Bus - Standard is the cheapest tier that supports topics
// (Basic does not), but Standard still has a real monthly cost - there
// is no free tier at all. deployServiceBus stays false until that cost
// is explicitly accepted; see day-24/README.md. ---
param serviceBusNamespaceName = 'sb-quoteshub-dev'
param serviceBusSkuName = 'Standard'
param serviceBusTopicName = 'quote-created'
param deployServiceBus = false // reverted for the day-27 subscription rebuild - see enableServiceBusIntegration comment above; not deploying without asking again

// --- Static Web App - Free is the cheapest tier that exists. Matches
// what is actually deployed today. ---
param staticWebAppName = 'swa-quotesui-thinkschool2'
param staticWebAppSkuTier = 'Free'
param staticWebAppRepositoryUrl = 'https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal'
param staticWebAppBranch = 'day17-deploy'

// --- Key Vault - created day-25 (rg-thinkschool-day17). Real name, not
// a placeholder: this vault exists. The JWT signing key is NOT sourced
// through this file at all any more - appservice.bicep builds a
// @Microsoft.KeyVault(SecretUri=...) reference directly from this
// vault's URI, and the App Service resolves it at runtime via its own
// managed identity. See day-25/README.md. ---
// Renamed from kv-quoteshub-dev (Day 25): that name is still held by a
// soft-deleted vault under the OLD (now-expired, different-tenant)
// subscription - Key Vault names are globally unique across all of
// Azure, and that vault's soft-delete record isn't visible or purgeable
// from this new tenant. kv-quoteshub-dev2 avoids the conflict.
param keyVaultName = 'kv-quoteshub-dev2'

// --- Application Insights - created day-26. Workspace-based (classic is
// retired). 30-day retention - the platform minimum - since this is a
// short-lived, cost-sensitive subscription. ---
param logAnalyticsWorkspaceName = 'law-quoteshub-dev'
param appInsightsName = 'appi-quoteshub-dev'
param logAnalyticsRetentionInDays = 30

// --- Secrets: never literals, never defaults. Pulled from the vault
// above via getSecret() - real subscription ID, real vault, real secret
// names populated day-25. ---
param sqlAdministratorPassword = getSecret('e55c32ce-f67a-4e77-b258-3a6dc2822724', 'rg-thinkschool-day17', 'kv-quoteshub-dev2', 'sql-admin-password')
// Service Bus is not adopted by the app yet (enableServiceBusIntegration
// is false above), but the parameter is still required - an empty
// secret is fine here, a literal empty string in this file is not.
param serviceBusConnectionString = getSecret('e55c32ce-f67a-4e77-b258-3a6dc2822724', 'rg-thinkschool-day17', 'kv-quoteshub-dev2', 'servicebus-connection-string')

// staticWebAppRepositoryToken is left unassigned: it defaults to '' in
// main.bicep and is only needed the first time this Static Web App is
// ever created (already true here - see README).
