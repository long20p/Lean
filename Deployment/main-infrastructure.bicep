// ==============================================================================
// Infrastructure Bicep Template - Lean Trading Engine Azure Deployment
// ==============================================================================
// Stage 1: Deploys infrastructure without container instance
// This allows us to build and push the image before deploying the container.
// ==============================================================================

targetScope = 'subscription'

// ==============================================================================
// Parameters
// ==============================================================================

@description('The environment name (dev, staging, prod)')
@allowed([
  'dev'
  'staging'
  'prod'
])
param environment string

@description('The Azure region for deployment')
param location string = 'eastus'

@description('Tags to apply to all resources')
param tags object = {
  Application: 'QuantConnect-Lean'
  ManagedBy: 'Bicep'
  Owner: 'LP'
}

@description('Enable diagnostic logging')
param enableDiagnostics bool = true

@description('Key Vault secrets to configure')
@secure()
param secrets object = {}

// ==============================================================================
// Variables
// ==============================================================================

var locationShort = location == 'eastus' ? 'eus' : location == 'westus' ? 'wus' : location == 'westeurope' ? 'weu' : location
var resourceGroupName = 'rg-lp-lean-${environment}-${locationShort}'
var containerRegistryName = 'acrlplean${environment}${locationShort}'
var storageAccountName = 'stlplean${environment}${locationShort}'
var logAnalyticsName = 'log-lp-lean-${environment}-${locationShort}'
var keyVaultName = 'kv-lp-lean-${environment}-${locationShort}'

var combinedTags = union(tags, {
  Environment: environment
  Location: location
})

// ==============================================================================
// Resource Group
// ==============================================================================

resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' = {
  name: resourceGroupName
  location: location
  tags: combinedTags
}

// ==============================================================================
// Modules
// ==============================================================================

// Log Analytics Workspace (deployed first for diagnostics)
module logAnalytics 'modules/log-analytics.bicep' = {
  scope: resourceGroup
  name: 'logAnalyticsDeploy'
  params: {
    name: logAnalyticsName
    location: location
    tags: combinedTags
    retentionInDays: environment == 'prod' ? 90 : 30
  }
}

// Key Vault for secrets
module keyVault 'modules/key-vault.bicep' = {
  scope: resourceGroup
  name: 'keyVaultDeploy'
  params: {
    name: keyVaultName
    location: location
    tags: combinedTags
    enabledForDeployment: true
    enableRbacAuthorization: true
    logAnalyticsWorkspaceId: enableDiagnostics ? logAnalytics.outputs.workspaceResourceId : ''
    secrets: secrets
  }
}

// Storage Account for data and results
module storage 'modules/storage-account.bicep' = {
  scope: resourceGroup
  name: 'storageDeploy'
  params: {
    name: storageAccountName
    location: location
    tags: combinedTags
    fileShareName: 'fs-lp-lean-data'
    fileShareQuota: 100
    logAnalyticsWorkspaceId: enableDiagnostics ? logAnalytics.outputs.workspaceResourceId : ''
  }
}

// Azure Container Registry
module containerRegistry 'modules/container-registry.bicep' = {
  scope: resourceGroup
  name: 'acrDeploy'
  params: {
    name: containerRegistryName
    location: location
    tags: combinedTags
    sku: 'Basic'
    adminUserEnabled: true
    logAnalyticsWorkspaceId: enableDiagnostics ? logAnalytics.outputs.workspaceResourceId : ''
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output resourceGroupName string = resourceGroup.name
output containerRegistryName string = containerRegistry.outputs.name
output containerRegistryLoginServer string = containerRegistry.outputs.loginServer
output containerRegistryUsername string = containerRegistry.outputs.username
output containerRegistryPassword string = containerRegistry.outputs.password
output storageAccountName string = storage.outputs.name
output fileShareName string = storage.outputs.fileShareName
output logAnalyticsWorkspaceId string = logAnalytics.outputs.workspaceId
output logAnalyticsWorkspaceKey string = logAnalytics.outputs.workspaceKey
output logAnalyticsWorkspaceResourceId string = logAnalytics.outputs.workspaceResourceId
output keyVaultName string = keyVault.outputs.name
output keyVaultUri string = keyVault.outputs.uri
