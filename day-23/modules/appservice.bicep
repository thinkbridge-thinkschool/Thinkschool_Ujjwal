@description('Name of the Linux App Service Plan.')
param planName string

@description('Name of the Web App (API host) - must be globally unique across Azure.')
param webAppName string

@description('Azure region for the plan and the web app.')
param location string

@description('App Service Plan SKU name, e.g. F1 (free) or P1v3 (production).')
param skuName string

@description('App Service Plan SKU tier, e.g. Free or PremiumV3.')
param skuTier string

@description('Number of instances in the plan.')
param skuCapacity int = 1

@description('Whether Always On is enabled. Must be false on the F1 (Free) tier - Azure rejects true there.')
param alwaysOn bool = false

@description('.NET runtime version for the Linux web app, e.g. "10.0".')
param dotnetVersion string = '10.0'

@description('ASPNETCORE_ENVIRONMENT app setting value.')
@allowed(['Development', 'Staging', 'Production'])
param aspnetCoreEnvironment string

@description('Origin allowed by the API CORS policy - the deployed Static Web App\'s URL.')
param corsAllowedOrigin string

@description('Entra (Azure AD) application/client ID the API validates bearer tokens against. Not a secret - already public in the app\'s own appsettings.json.')
param entraAudience string

@description('Base URI of the Key Vault holding secrets this app reads at runtime, e.g. https://kv-quoteshub-dev.vault.azure.net/. Used only to build a Key Vault reference string for an app setting - never a secret value itself, and the JWT signing key never passes through this template as a parameter at all (day-25/README.md).')
param keyVaultUri string

@description('Name of the secret in Key Vault holding the JWT signing key.')
param jwtKeySecretName string = 'jwt-key'

@description('SQL Server connection string for QuotesDbContext. Secure - supplied at deploy time, never a literal or default.')
@secure()
param sqlConnectionString string

@description('Whether to wire the Service Bus connection string in as an app setting. False until the app actually adopts real Azure Service Bus (see day-23/README.md) - keeping this off matches what is deployed today.')
param enableServiceBusIntegration bool = false

@description('Service Bus connection string. Only wired into an app setting when enableServiceBusIntegration is true, but always required as an input (even as an empty string) - never a literal or default. Secure - supplied at deploy time.')
@secure()
param serviceBusConnectionString string

@description('Tags applied to the plan and the web app.')
param tags object = {}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
    tier: skuTier
    capacity: skuCapacity
  }
  properties: {
    reserved: true
  }
}

// Modeled as a single Microsoft.Web/sites resource with appSettings
// inline (not a separate Microsoft.Web/sites/config child resource) so
// this list is the complete, authoritative set of settings - deploying
// it replaces whatever is actually configured rather than merging with
// it, which is what makes "does this match reality" a meaningful
// question for what-if to answer.
resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  // System-assigned identity - what the Key Vault Secrets User role
  // assignment in main.bicep grants access to. Nothing else in this
  // template depends on it yet (day-25/README.md covers what SQL/Service
  // Bus access via this same identity would need, deliberately not built
  // here).
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|${dotnetVersion}'
      alwaysOn: alwaysOn
      ftpsState: 'FtpsOnly'
      minTlsVersion: '1.2'
      appSettings: concat(
        [
          { name: 'ASPNETCORE_ENVIRONMENT', value: aspnetCoreEnvironment }
          { name: 'Cors__AllowedOrigin', value: corsAllowedOrigin }
          { name: 'Entra__Audience', value: entraAudience }
          // Unversioned reference - App Service resolves this to
          // whatever the secret's CURRENT version is on every read, so
          // rotating the key in Key Vault later needs no redeploy here.
          { name: 'Jwt__Key', value: '@Microsoft.KeyVault(SecretUri=${keyVaultUri}secrets/${jwtKeySecretName}/)' }
          { name: 'ConnectionStrings__Default', value: sqlConnectionString }
        ],
        enableServiceBusIntegration ? [{ name: 'ServiceBus__ConnectionString', value: serviceBusConnectionString }] : []
      )
    }
  }
}

output webAppName string = webApp.name
output webAppHostName string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
