// ==============================================================================
// Key Vault Module
// ==============================================================================

@description('The name of the Key Vault')
param name string

@description('The location for the Key Vault')
param location string

@description('Tags to apply to the resource')
param tags object

@description('Enable for deployment')
param enabledForDeployment bool = false

@description('Enable for disk encryption')
param enabledForDiskEncryption bool = false

@description('Enable for template deployment')
param enabledForTemplateDeployment bool = true

@description('Enable RBAC authorization')
param enableRbacAuthorization bool = true

@description('Log Analytics workspace ID for diagnostics')
param logAnalyticsWorkspaceId string = ''

@description('Secrets to create in the Key Vault')
@secure()
param secrets object = {}

// ==============================================================================
// Resources
// ==============================================================================

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: name
  location: location
  tags: tags
  properties: {
sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enabledForDeployment: enabledForDeployment
    enabledForDiskEncryption: enabledForDiskEncryption
    enabledForTemplateDeployment: enabledForTemplateDeployment
    enableRbacAuthorization: enableRbacAuthorization
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    networkAcls: {
    bypass: 'AzureServices'
    defaultAction: 'Allow'
    }
    publicNetworkAccess: 'Enabled'
  }
}

// Create secrets
resource keyVaultSecrets 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = [for secret in items(secrets): {
  parent: keyVault
  name: secret.key
  properties: {
    value: secret.value
  }
}]

// Diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  scope: keyVault
  name: '${name}-diagnostics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
    category: 'AuditEvent'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output id string = keyVault.id
output name string = keyVault.name
output uri string = keyVault.properties.vaultUri
