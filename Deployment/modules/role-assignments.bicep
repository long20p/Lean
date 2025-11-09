// ==============================================================================
// Role Assignments Module
// ==============================================================================
// Assigns necessary roles to the container instance managed identity

@description('Principal ID of the container instance managed identity')
param containerInstancePrincipalId string

@description('Container Registry name')
param containerRegistryName string

@description('Storage Account name')
param storageAccountName string

@description('Key Vault name')
param keyVaultName string

// ==============================================================================
// Existing Resources
// ==============================================================================

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-01-01-preview' existing = {
  name: containerRegistryName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' existing = {
  name: keyVaultName
}

// ==============================================================================
// Role Definitions
// ==============================================================================

// AcrPull role - allows pulling images from ACR
var acrPullRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

// Storage File Data SMB Share Contributor - allows read/write to file shares
var storageFileDataSmbShareContributorRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '0c867c2a-1d8c-454a-a3db-ab2ea1bdc8bb')

// Key Vault Secrets User - allows reading secrets
var keyVaultSecretsUserRoleId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

// ==============================================================================
// Role Assignments
// ==============================================================================

// Assign AcrPull role to container instance for pulling images
resource acrPullRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(containerRegistry.id, containerInstancePrincipalId, acrPullRoleId)
  scope: containerRegistry
  properties: {
    roleDefinitionId: acrPullRoleId
    principalId: containerInstancePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Assign Storage File Data SMB Share Contributor role for file share access
resource storageFileRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, containerInstancePrincipalId, storageFileDataSmbShareContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageFileDataSmbShareContributorRoleId
    principalId: containerInstancePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Assign Key Vault Secrets User role for reading secrets
resource keyVaultSecretsRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, containerInstancePrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalId: containerInstancePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output acrPullRoleAssignmentId string = acrPullRoleAssignment.id
output storageFileRoleAssignmentId string = storageFileRoleAssignment.id
output keyVaultSecretsRoleAssignmentId string = keyVaultSecretsRoleAssignment.id
