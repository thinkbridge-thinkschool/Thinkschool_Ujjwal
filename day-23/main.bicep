targetScope = 'resourceGroup'

@description('Azure region for every resource.')
param location string = resourceGroup().location

@description('Environment name - used only for tagging, not for branching logic. The actual dev/prod difference lives entirely in params/*.bicepparam.')
@allowed(['dev', 'prod'])
param environmentName string

// --- App Service (the API host) ---
@description('App Service Plan name.')
param appServicePlanName string
@description('Web App (API) name - must be globally unique.')
param webAppName string
@description('App Service Plan SKU name, e.g. F1 (free) or P1v3 (production).')
param appServiceSkuName string
@description('App Service Plan SKU tier, e.g. Free or PremiumV3.')
param appServiceSkuTier string
@description('App Service Plan instance count.')
param appServiceSkuCapacity int = 1
@description('Whether Always On is enabled. Must be false on the F1 (Free) tier.')
param appServiceAlwaysOn bool = false
@description('.NET runtime version for the Linux web app.')
param dotnetVersion string = '10.0'
@description('ASPNETCORE_ENVIRONMENT app setting value.')
@allowed(['Development', 'Staging', 'Production'])
param aspnetCoreEnvironment string
@description('Entra (Azure AD) application/client ID the API validates bearer tokens against. Not a secret.')
param entraAudience string
@description('JWT signing key. Secure - supplied at deploy time, never a literal or default.')
@secure()
param jwtKey string
@description('Whether to wire a Service Bus connection string into the API. False until the app actually adopts real Azure Service Bus - see README.')
param enableServiceBusIntegration bool = false

// --- Azure SQL ---
@description('Azure SQL logical server name - must be globally unique.')
param sqlServerName string
@description('SQL administrator login name.')
param sqlAdministratorLogin string
@description('SQL administrator password. Secure - supplied at deploy time, never a literal or default.')
@secure()
param sqlAdministratorPassword string
@description('Database name.')
param sqlDatabaseName string
@description('Database SKU tier, e.g. GeneralPurpose.')
param sqlSkuTier string
@description('Database SKU name, e.g. GP_S_Gen5 (serverless, dev) or GP_Gen5 (provisioned, prod).')
param sqlSkuName string
@description('Database SKU family.')
param sqlSkuFamily string = 'Gen5'
@description('Database vCore capacity.')
param sqlSkuCapacity int
@description('Maximum database size in bytes.')
param sqlMaxSizeBytes int = 34359738368
@description('Whether this database uses the Azure SQL free monthly limit offer (allowed on only one database per subscription).')
param sqlUseFreeLimit bool = false

// --- Service Bus ---
@description('Service Bus namespace name - must be globally unique.')
param serviceBusNamespaceName string
@description('Service Bus SKU tier - Standard is the cheapest tier that supports topics (Basic does not).')
@allowed(['Standard', 'Premium'])
param serviceBusSkuName string
@description('Premium messaging units. Ignored on Standard.')
param serviceBusPremiumMessagingUnits int = 1
@description('Topic name quote-created events are published to.')
param serviceBusTopicName string = 'quote-created'
@description('Service Bus connection string. Always required (even as an empty string) when enableServiceBusIntegration is false - never a literal or default. Secure - supplied at deploy time.')
@secure()
param serviceBusConnectionString string

// --- Static Web App (the frontend) ---
@description('Static Web App name - must be globally unique.')
param staticWebAppName string
@description('Static Web App SKU tier.')
@allowed(['Free', 'Standard'])
param staticWebAppSkuTier string = 'Free'
@description('GitHub repository URL the Static Web App deploys from.')
param staticWebAppRepositoryUrl string
@description('Branch the Static Web App tracks.')
param staticWebAppBranch string
@description('GitHub token for the repository connection. Only required on first creation - leave empty on later updates. Secure - supplied at deploy time, never a literal or default.')
@secure()
param staticWebAppRepositoryToken string = ''

@description('Tags applied to every resource.')
param tags object = {
  environment: environmentName
  project: 'quoteshub'
}

module sql 'modules/sql.bicep' = {
  name: 'sql-deployment'
  params: {
    serverName: sqlServerName
    location: location
    administratorLogin: sqlAdministratorLogin
    administratorLoginPassword: sqlAdministratorPassword
    databaseName: sqlDatabaseName
    skuTier: sqlSkuTier
    skuName: sqlSkuName
    skuFamily: sqlSkuFamily
    skuCapacity: sqlSkuCapacity
    maxSizeBytes: sqlMaxSizeBytes
    useFreeLimit: sqlUseFreeLimit
    tags: tags
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'servicebus-deployment'
  params: {
    namespaceName: serviceBusNamespaceName
    location: location
    skuName: serviceBusSkuName
    premiumMessagingUnits: serviceBusPremiumMessagingUnits
    topicName: serviceBusTopicName
    tags: tags
  }
}

module staticWebApp 'modules/staticwebapp.bicep' = {
  name: 'staticwebapp-deployment'
  params: {
    name: staticWebAppName
    location: location
    skuTier: staticWebAppSkuTier
    repositoryUrl: staticWebAppRepositoryUrl
    branch: staticWebAppBranch
    repositoryToken: staticWebAppRepositoryToken
    tags: tags
  }
}

// The API's CORS origin and DB connection string both come from sibling
// modules' outputs (the SWA's real hostname, the SQL server's real FQDN)
// rather than being duplicated as separate literal parameters - one
// source of truth for each, no risk of the two drifting apart.
module appService 'modules/appservice.bicep' = {
  name: 'appservice-deployment'
  params: {
    planName: appServicePlanName
    webAppName: webAppName
    location: location
    skuName: appServiceSkuName
    skuTier: appServiceSkuTier
    skuCapacity: appServiceSkuCapacity
    alwaysOn: appServiceAlwaysOn
    dotnetVersion: dotnetVersion
    aspnetCoreEnvironment: aspnetCoreEnvironment
    corsAllowedOrigin: 'https://${staticWebApp.outputs.defaultHostname}'
    entraAudience: entraAudience
    jwtKey: jwtKey
    sqlConnectionString: 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sqlDatabaseName};User Id=${sqlAdministratorLogin};Password=${sqlAdministratorPassword};Encrypt=True;TrustServerCertificate=False;'
    enableServiceBusIntegration: enableServiceBusIntegration
    serviceBusConnectionString: serviceBusConnectionString
  }
}

output webAppHostName string = appService.outputs.webAppHostName
output staticWebAppHostName string = staticWebApp.outputs.defaultHostname
output sqlServerFqdn string = sql.outputs.serverFqdn
output serviceBusNamespaceName string = serviceBus.outputs.namespaceName
