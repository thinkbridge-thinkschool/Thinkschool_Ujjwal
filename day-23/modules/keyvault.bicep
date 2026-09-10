@description('Name of the Key Vault - must be globally unique across Azure (it becomes part of the vault DNS name).')
param vaultName string

@description('Azure region for the vault.')
param location string

@description('Azure AD tenant that owns this vault.')
param tenantId string = subscription().tenantId

@description('SKU. Standard is sufficient for secrets - Premium only adds HSM-backed keys, which nothing here needs.')
@allowed(['standard', 'premium'])
param skuName string = 'standard'

@description('Tags applied to the vault.')
param tags object = {}

// RBAC authorization, not the legacy access-policy model - "grant Key
// Vault Secrets User" (main.bicep) is an Azure RBAC role assignment,
// which only exists as a concept once enableRbacAuthorization is true.
// No secrets are created here: this module only stands up the vault
// itself. Secret values are set directly via `az keyvault secret set`,
// never passed through a Bicep parameter - see day-25/README.md for why.
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      name: skuName
    }
    tenantId: tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

output vaultName string = vault.name
output vaultUri string = vault.properties.vaultUri
