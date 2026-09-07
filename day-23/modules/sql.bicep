@description('Name of the Azure SQL logical server - must be globally unique across Azure.')
param serverName string

@description('Azure region for the server and database.')
param location string

@description('SQL Server administrator login name.')
param administratorLogin string

@description('SQL Server administrator password. Secure - supplied at deploy time (Key Vault reference or --parameters), never a literal or default.')
@secure()
param administratorLoginPassword string

@description('Name of the database.')
param databaseName string

@description('Database SKU tier, e.g. GeneralPurpose or BusinessCritical.')
param skuTier string

@description('Database SKU name, e.g. GP_S_Gen5 (serverless, cheapest) or GP_Gen5 (provisioned, production).')
param skuName string

@description('Database SKU family.')
param skuFamily string = 'Gen5'

@description('Number of vCores.')
param skuCapacity int

@description('Maximum database size in bytes.')
param maxSizeBytes int = 34359738368

@description('Whether this database uses the Azure SQL free monthly limit offer. Azure allows this on only one database per subscription.')
param useFreeLimit bool = false

@description('Behavior once the free limit is exhausted for the month. Only meaningful when useFreeLimit is true.')
@allowed(['AutoPause', 'BillForUsage'])
param freeLimitExhaustionBehavior string = 'AutoPause'

@description('Server firewall rules to create, as an array of {name, startIpAddress, endIpAddress} objects. Use 0.0.0.0-0.0.0.0 for the "allow Azure services" rule.')
param firewallRules array = [
  {
    name: 'AllowAzureServices'
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
]

@description('Tags applied to the server and the database.')
param tags object = {}

resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorLoginPassword
    version: '12.0'
  }
}

resource rules 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = [
  for rule in firewallRules: {
    parent: server
    name: rule.name
    properties: {
      startIpAddress: rule.startIpAddress
      endIpAddress: rule.endIpAddress
    }
  }
]

resource database 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
    family: skuFamily
    capacity: skuCapacity
  }
  properties: useFreeLimit
    ? {
        maxSizeBytes: maxSizeBytes
        useFreeLimit: true
        freeLimitExhaustionBehavior: freeLimitExhaustionBehavior
      }
    : {
        maxSizeBytes: maxSizeBytes
        useFreeLimit: false
      }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
