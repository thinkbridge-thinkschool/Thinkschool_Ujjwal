@description('Name of the virtual network.')
param vnetName string

@description('Azure region - must match the App Service and SQL server this is meant to connect.')
param location string

@description('Address space for the whole VNet, e.g. 10.20.0.0/24.')
param addressPrefix string = '10.20.0.0/24'

@description('Subnet delegated to Microsoft.Web/serverFarms for App Service regional VNet integration. Minimum /28; /27 leaves headroom.')
param integrationSubnetPrefix string = '10.20.0.0/27'

@description('Subnet the SQL private endpoint\'s NIC lives in. Not delegated - private endpoints attach to a plain subnet.')
param privateEndpointSubnetPrefix string = '10.20.0.32/27'

@description('Resource ID of the existing SQL server to put behind a private endpoint.')
param sqlServerId string

@description('Tags applied to every resource in this module.')
param tags object = {}

// day-27: written as the design-ready answer to "private endpoints for
// SQL" - NOT deployed. F1 (the App Service tier actually running today)
// cannot use VNet integration at all; that requires Basic (B1) or above,
// a real ~$14.60/month cost (East Asia, confirmed against Azure's own
// retail pricing API, not assumed) that was not spent without asking
// first. See day-27/README.md for the interim mitigation that WAS
// deployed (SQL firewall tightened to Azure services + one IP) while
// this stays on the shelf.
resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: 'integration-subnet'
        properties: {
          addressPrefix: integrationSubnetPrefix
          delegations: [
            {
              name: 'appServiceDelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoint-subnet'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          // Required for a subnet to host private endpoints - without
          // this, the subnet inherits the default that blocks them.
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// Without this, the App Service (once VNet-integrated) would resolve the
// SQL server's public FQDN to its public IP as normal - the private
// endpoint would exist but nothing would actually route traffic through
// it. This zone plus the VNet link plus the private DNS zone group below
// are what make DNS resolution INSIDE this VNet return the private IP
// instead.
resource privateDnsZone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: 'privatelink.database.windows.net'
  location: 'global'
  tags: tags
}

resource privateDnsZoneVnetLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: privateDnsZone
  name: '${vnetName}-link'
  location: 'global'
  tags: tags
  properties: {
    virtualNetwork: {
      id: vnet.id
    }
    registrationEnabled: false
  }
}

resource sqlPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-11-01' = {
  name: '${vnetName}-sql-pe'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: '${vnet.id}/subnets/private-endpoint-subnet'
    }
    privateLinkServiceConnections: [
      {
        name: 'sqlConnection'
        properties: {
          privateLinkServiceId: sqlServerId
          groupIds: ['sqlServer']
        }
      }
    ]
  }
}

resource sqlPrivateDnsZoneGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-11-01' = {
  parent: sqlPrivateEndpoint
  name: 'sql-dns-zone-group'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'privatelink-database-windows-net'
        properties: {
          privateDnsZoneId: privateDnsZone.id
        }
      }
    ]
  }
}

@description('Subnet ID to pass into appservice.bicep\'s virtualNetworkSubnetId parameter once the App Service Plan is upgraded to Basic or above.')
output integrationSubnetId string = '${vnet.id}/subnets/integration-subnet'
output vnetId string = vnet.id
