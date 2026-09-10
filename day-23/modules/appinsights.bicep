@description('Name of the Log Analytics workspace backing this Application Insights resource - must be globally unique-ish (unique within the resource group is enough, but Azure recommends account-wide uniqueness).')
param workspaceName string

@description('Name of the Application Insights component.')
param appInsightsName string

@description('Azure region for both resources.')
param location string

@description('Data retention in days. Deliberately set to 30 - the minimum the PerGB2018 SKU allows - not the 90-day default. This is a short-lived, cost-sensitive project (the subscription itself expires in days), so there is no reason to allocate or pay for retention longer than the minimum the platform permits.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Tags applied to both resources.')
param tags object = {}

// Workspace-based Application Insights - classic (non-workspace)
// Application Insights is retired for new resources, so this is the
// only option, not a preference. The workspace is the actual log store;
// the Application Insights component is effectively a view/API surface
// over it plus the SDK-facing connection string.
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
    RetentionInDays: retentionInDays
  }
}

// Not a secret: Microsoft's own Key Vault reference documentation
// explicitly calls out APPLICATIONINSIGHTS_CONNECTION_STRING as a value
// that "isn't considered a secret" - it identifies which telemetry
// resource to send to, it doesn't grant read access to anything by
// itself. Wired as a plain app setting sourced from this output, not a
// literal anywhere in the repo (main.bicep), and not routed through Key
// Vault the way Jwt__Key is (day-25/README.md).
output connectionString string = appInsights.properties.ConnectionString
output appInsightsName string = appInsights.name
output workspaceName string = workspace.name
