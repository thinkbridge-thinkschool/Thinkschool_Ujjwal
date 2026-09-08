using '../main.bicep'

param location = 'centralus'
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
param webAppName = 'quotesapi-thinkschool-prod'
param appServiceSkuName = 'F1'
param appServiceSkuTier = 'Free'
param appServiceSkuCapacity = 1
param appServiceAlwaysOn = false // F1 does not support Always On
param dotnetVersion = '10.0'
param aspnetCoreEnvironment = 'Production'
param entraAudience = '9595ac6d-99d5-42b5-bffe-fed74bce6f42'
param enableServiceBusIntegration = false // Service Bus is not being deployed at all right now - see below

// --- Azure SQL - REDUCED FOR COST (day-24): serverless GP_S_Gen5, same
// family as dev, instead of a provisioned GP_Gen5 with 4 fixed vCores.
// useFreeLimit stays false here on purpose - Azure allows the free-limit
// offer on only ONE database per subscription, and dev's database
// already claims it. This is therefore "near-free" rather than free:
// serverless auto-pauses to zero compute cost when idle, leaving only a
// small storage charge (well under $1/month for a near-empty database).
// In reality this row would be:
//   sqlSkuName = 'GP_Gen5' (provisioned, not serverless), sqlSkuCapacity = 4,
//   sqlMaxSizeBytes = 137438953472 (128 GB)
// (steady-state production load doesn't want serverless's cold-start
// pause behavior, and a fixed vCore count is predictable to budget for).
param sqlServerName = 'sql-quotesapi-prod'
param sqlAdministratorLogin = 'quoteshubadmin'
param sqlDatabaseName = 'quoteshub'
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
param staticWebAppName = 'swa-quotesui-prod'
param staticWebAppSkuTier = 'Free'
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
