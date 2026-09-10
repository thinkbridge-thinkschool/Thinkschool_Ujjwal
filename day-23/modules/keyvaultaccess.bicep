@description('Name of an existing Key Vault to grant access on.')
param vaultName string

@description('Principal ID (object ID) of the identity being granted access, e.g. a Web App system-assigned managed identity.')
param principalId string

@description('Type of the principal - set explicitly rather than left for ARM to look up, since a just-created identity in the same deployment may not have replicated through Azure AD yet.')
@allowed(['ServicePrincipal', 'User', 'Group'])
param principalType string = 'ServicePrincipal'

// Key Vault Secrets User (built-in role 4633458b-17de-408a-b874-0445c86b69e6):
// read secret VALUES only. No key/certificate access, no vault
// management, no write access to secrets either - least privilege for an
// app that only ever needs to read one secret at runtime.
//
// This is its own module, separate from keyvault.bicep, specifically to
// avoid a circular dependency: this role assignment needs the App
// Service's principalId (only available after that module runs), while
// appservice.bicep needs the vault's URI (only available after the
// vault is created) - if the role assignment lived inside keyvault.bicep
// and took principalId as a parameter, the ENTIRE vault module (vault
// creation included) would end up waiting on appService, which itself
// waits on the vault. Two separate modules break that cycle.
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: vaultName
}

resource roleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, principalId, 'KeyVaultSecretsUser')
  scope: vault
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}
