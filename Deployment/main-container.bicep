// ==============================================================================
// Container Instance Bicep Template - Lean Trading Engine Azure Deployment
// ==============================================================================
// Stage 3: Deploys the container instance after infrastructure and image are ready
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

@description('The algorithm type name to run')
param algorithmTypeName string = 'ExperimentalAlgorithm'

@description('The algorithm language (CSharp or Python)')
@allowed([
  'CSharp'
  'Python'
])
param algorithmLanguage string = 'CSharp'

@description('Container CPU cores')
param containerCpu int = environment == 'prod' ? 2 : 1

@description('Container memory in GB')
param containerMemoryInGb int = environment == 'prod' ? 4 : 2

@description('Container image tag')
param imageTag string = 'latest'

@description('Container registry login server')
param containerRegistryServer string

@description('Container registry username')
param containerRegistryUsername string

@description('Container registry password')
@secure()
param containerRegistryPassword string

@description('Storage account name')
param storageAccountName string

@description('File share name')
param fileShareName string

@description('Log Analytics workspace ID')
param logAnalyticsWorkspaceId string

@description('Log Analytics workspace key')
@secure()
param logAnalyticsWorkspaceKey string

@description('Key Vault name')
param keyVaultName string

// ==============================================================================
// Variables
// ==============================================================================

var locationShort = location == 'eastus' ? 'eus' : location == 'westus' ? 'wus' : location == 'westeurope' ? 'weu' : location
var resourceGroupName = 'rg-lp-lean-${environment}-${locationShort}'
var containerInstanceName = 'aci-lp-lean-${environment}-${locationShort}'
var containerRegistryName = 'acrlplean${environment}${locationShort}'

var combinedTags = union(tags, {
  Environment: environment
  Location: location
})

// ==============================================================================
// Resource Group
// ==============================================================================

resource resourceGroup 'Microsoft.Resources/resourceGroups@2021-04-01' existing = {
  name: resourceGroupName
}

// ==============================================================================
// Modules
// ==============================================================================

// Azure Container Instance
module containerInstance 'modules/container-instance.bicep' = {
  scope: resourceGroup
  name: 'aciDeploy'
  params: {
    name: containerInstanceName
    location: location
    tags: combinedTags
    containerRegistryServer: containerRegistryServer
    containerRegistryUsername: containerRegistryUsername
    containerRegistryPassword: containerRegistryPassword
    imageName: 'lean-custom'
    imageTag: imageTag
    cpu: containerCpu
    memoryInGb: containerMemoryInGb
    storageAccountName: storageAccountName
    fileShareName: fileShareName
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    logAnalyticsWorkspaceKey: logAnalyticsWorkspaceKey
    algorithmTypeName: algorithmTypeName
    algorithmLanguage: algorithmLanguage
    environment: environment
    restartPolicy: environment == 'prod' ? 'Always' : 'Never'
  }
}

// Role assignments for managed identity
module roleAssignments 'modules/role-assignments.bicep' = {
  scope: resourceGroup
  name: 'roleAssignmentsDeploy'
  params: {
    containerInstancePrincipalId: containerInstance.outputs.principalId
    containerRegistryName: containerRegistryName
    storageAccountName: storageAccountName
    keyVaultName: keyVaultName
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output containerInstanceName string = containerInstance.outputs.name
output containerInstanceFqdn string = containerInstance.outputs.fqdn
output containerInstancePrincipalId string = containerInstance.outputs.principalId
