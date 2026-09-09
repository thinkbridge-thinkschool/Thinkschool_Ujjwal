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
param deployServiceBus = false

// --- Static Web App - Free is the cheapest tier that exists. Matches
// what is actually deployed today. ---
param staticWebAppName = 'swa-quotesui-thinkschool'
param staticWebAppSkuTier = 'Free'
param staticWebAppRepositoryUrl = 'https://github.com/thinkbridge-thinkschool/Thinkschool_Ujjwal'
param staticWebAppBranch = 'day17-deploy'

// --- Key Vault - created day-25 (rg-thinkschool-day17). Real name, not
// a placeholder: this vault exists. The JWT signing key is NOT sourced
// through this file at all any more - appservice.bicep builds a
// @Microsoft.KeyVault(SecretUri=...) reference directly from this
// vault's URI, and the App Service resolves it at runtime via its own
// managed identity. See day-25/README.md. ---
param keyVaultName = 'kv-quoteshub-dev'

// --- Secrets: never literals, never defaults. Pulled from the vault
// above via getSecret() - real subscription ID, real vault, real secret
// names populated day-25. ---
param sqlAdministratorPassword = getSecret('4137ee92-b41e-4d41-a149-6aa8a5a08aba', 'rg-thinkschool-day17', 'kv-quoteshub-dev', 'sql-admin-password')
// Service Bus is not adopted by the app yet (enableServiceBusIntegration
// is false above), but the parameter is still required - an empty
// secret is fine here, a literal empty string in this file is not.
param serviceBusConnectionString = getSecret('4137ee92-b41e-4d41-a149-6aa8a5a08aba', 'rg-thinkschool-day17', 'kv-quoteshub-dev', 'servicebus-connection-string')

// staticWebAppRepositoryToken is left unassigned: it defaults to '' in
// main.bicep and is only needed the first time this Static Web App is
// ever created (already true here - see README).
