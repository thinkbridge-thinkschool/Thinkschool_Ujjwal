using '../main.bicep'

param location = 'centralus'
param environmentName = 'prod'

// --- App Service - P1v3 (PremiumV3): supports Always On, autoscale, and
// staging slots, none of which F1 allows. A realistic production tier,
// not the cheapest one. ---
param appServicePlanName = 'asp-quotesapi-prod'
param webAppName = 'quotesapi-thinkschool-prod'
param appServiceSkuName = 'P1v3'
param appServiceSkuTier = 'PremiumV3'
param appServiceSkuCapacity = 2 // two instances - no single point of failure
param appServiceAlwaysOn = true
param dotnetVersion = '10.0'
param aspnetCoreEnvironment = 'Production'
param entraAudience = '9595ac6d-99d5-42b5-bffe-fed74bce6f42'
param enableServiceBusIntegration = true // production is expected to have adopted real Service Bus by then

// --- Azure SQL - GP_Gen5 provisioned (not serverless): steady-state
// production load doesn't want serverless's cold-start pause behavior,
// and a fixed vCore count is predictable to budget for. ---
param sqlServerName = 'sql-quotesapi-prod'
param sqlAdministratorLogin = 'quoteshubadmin'
param sqlDatabaseName = 'quoteshub'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuName = 'GP_Gen5'
param sqlSkuFamily = 'Gen5'
param sqlSkuCapacity = 4
param sqlMaxSizeBytes = 137438953472 // 128 GB
param sqlUseFreeLimit = false // the free-limit offer is capped at one database per subscription - dev already claims it

// --- Service Bus - Premium: dedicated resource capacity and predictable
// latency instead of Standard's shared-tenant throughput. This is a real
// cost jump (fixed hourly charge regardless of traffic) - see README. ---
param serviceBusNamespaceName = 'sb-quoteshub-prod'
param serviceBusSkuName = 'Premium'
param serviceBusPremiumMessagingUnits = 1
param serviceBusTopicName = 'quote-created'

// --- Static Web App - Standard: custom domains with managed
// certificates beyond the default *.azurestaticapps.net one, and a
// larger app/API size limit than Free. ---
param staticWebAppName = 'swa-quotesui-prod'
param staticWebAppSkuTier = 'Standard'
param staticWebAppRepositoryUrl = 'https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal'
param staticWebAppBranch = 'main'

// --- Secrets: never literals, never defaults. Pulled from an existing
// Key Vault via getSecret() - the vault itself is NOT created by this
// Bicep (see README.md's "Secrets" section) and must already exist with
// these three secret names populated before this file can be used to
// deploy for real. Replace <subscription-id> with the target
// subscription's ID and kv-quoteshub-prod with the real vault name
// before use. A production vault should not be the same vault dev
// secrets live in. ---
param jwtKey = getSecret('<subscription-id>', 'rg-thinkschool-prod', 'kv-quoteshub-prod', 'jwt-key')
param sqlAdministratorPassword = getSecret('<subscription-id>', 'rg-thinkschool-prod', 'kv-quoteshub-prod', 'sql-admin-password')
param serviceBusConnectionString = getSecret('<subscription-id>', 'rg-thinkschool-prod', 'kv-quoteshub-prod', 'servicebus-connection-string')

// staticWebAppRepositoryToken is left unassigned: it defaults to '' in
// main.bicep. Only required the first time swa-quotesui-prod is ever
// created - supply it once, out-of-band, on that single deployment.
