using '../main.bicep'

param location = 'centralus'
param environmentName = 'dev'

// --- App Service - F1 is the cheapest tier that exists. This matches
// what is actually deployed today (rg-thinkschool-day17). ---
param appServicePlanName = 'asp-quotesapi-free'
param webAppName = 'quotesapi-thinkschool'
param appServiceSkuName = 'F1'
param appServiceSkuTier = 'Free'
param appServiceSkuCapacity = 1
param appServiceAlwaysOn = false // F1 does not support Always On
param dotnetVersion = '10.0'
param aspnetCoreEnvironment = 'Production' // matches the app setting actually configured today
param entraAudience = '9595ac6d-99d5-42b5-bffe-fed74bce6f42' // public - see InfrastructureExtensions.cs comment
param enableServiceBusIntegration = false // Service Bus is not adopted by the app yet - see README

// --- Azure SQL - GP_S_Gen5 serverless with the free-limit offer is the
// cheapest configuration that exists. Matches the database actually
// created in rg-thinkschool-day17 this session. ---
param sqlServerName = 'sql-quotesapi-thinkschool'
param sqlAdministratorLogin = 'quoteshubadmin'
param sqlDatabaseName = 'quoteshub'
param sqlSkuTier = 'GeneralPurpose'
param sqlSkuName = 'GP_S_Gen5'
param sqlSkuFamily = 'Gen5'
param sqlSkuCapacity = 2
param sqlMaxSizeBytes = 34359738368 // 32 GB
param sqlUseFreeLimit = true

// --- Service Bus - Standard is the cheapest tier that supports topics
// (Basic does not). Not deployed yet - see README. ---
param serviceBusNamespaceName = 'sb-quoteshub-dev'
param serviceBusSkuName = 'Standard'
param serviceBusTopicName = 'quote-created'

// --- Static Web App - Free is the cheapest tier that exists. Matches
// what is actually deployed today. ---
param staticWebAppName = 'swa-quotesui-thinkschool'
param staticWebAppSkuTier = 'Free'
param staticWebAppRepositoryUrl = 'https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal'
param staticWebAppBranch = 'day17-deploy'

// --- Secrets: never literals, never defaults. Pulled from an existing
// Key Vault via getSecret() - the vault itself is NOT created by this
// Bicep (see README.md's "Secrets" section) and must already exist with
// these three secret names populated before this file can be used to
// deploy for real. Replace <subscription-id> with the target
// subscription's ID and kv-quoteshub-dev with the real vault name before
// use. ---
param jwtKey = getSecret('<subscription-id>', 'rg-thinkschool-day17', 'kv-quoteshub-dev', 'jwt-key')
param sqlAdministratorPassword = getSecret('<subscription-id>', 'rg-thinkschool-day17', 'kv-quoteshub-dev', 'sql-admin-password')
// Service Bus is not adopted by the app yet (enableServiceBusIntegration
// is false above), but the parameter is still required - an empty
// secret is fine here, a literal empty string in this file is not.
param serviceBusConnectionString = getSecret('<subscription-id>', 'rg-thinkschool-day17', 'kv-quoteshub-dev', 'servicebus-connection-string')

// staticWebAppRepositoryToken is left unassigned: it defaults to '' in
// main.bicep and is only needed the first time this Static Web App is
// ever created (already true here - see README).
