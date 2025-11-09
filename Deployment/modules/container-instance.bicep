// ==============================================================================
// Azure Container Instance Module
// ==============================================================================

@description('The name of the container instance')
param name string

@description('The location for the container instance')
param location string

@description('Tags to apply to the resource')
param tags object

@description('Container Registry server')
param containerRegistryServer string

@description('Container Registry username')
param containerRegistryUsername string

@description('Container Registry password')
@secure()
param containerRegistryPassword string

@description('The Docker image name')
param imageName string

@description('The Docker image tag')
param imageTag string = 'latest'

@description('Number of CPU cores')
param cpu int = 1

@description('Memory in GB')
param memoryInGb int = 2

@description('Storage account name for file share')
param storageAccountName string

@description('File share name')
param fileShareName string

@description('Log Analytics workspace ID')
param logAnalyticsWorkspaceId string

@description('Log Analytics workspace key')
@secure()
param logAnalyticsWorkspaceKey string

@description('Algorithm type name')
param algorithmTypeName string

@description('Algorithm language')
param algorithmLanguage string

@description('Environment name')
param environment string

@description('Container restart policy')
@allowed([
  'Always'
  'OnFailure'
  'Never'
])
param restartPolicy string = 'OnFailure'

// ==============================================================================
// Existing Resources
// ==============================================================================

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// ==============================================================================
// Resources
// ==============================================================================

resource containerGroup 'Microsoft.ContainerInstance/containerGroups@2023-05-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    containers: [
      {
        name: 'lean-engine'
        properties: {
          image: '${containerRegistryServer}/${imageName}:${imageTag}'
          ports: []
          environmentVariables: [
            {
              name: 'ENVIRONMENT'
              value: environment
            }
            {
              name: 'ALGORITHM_TYPE_NAME'
              value: algorithmTypeName
            }
            {
              name: 'ALGORITHM_LANGUAGE'
              value: algorithmLanguage
            }
            {
              name: 'CONFIG_FILE'
              value: '/Lean/config.json'
            }
            {
              name: 'RESULTS_PATH'
              value: '/Results'
            }
            {
              name: 'DATA_PATH'
              value: '/Data'
            }
            {
              name: 'AZURE_STORAGE_ACCOUNT'
              value: storageAccountName
            }
          ]
          resources: {
            requests: {
              cpu: cpu
              memoryInGB: memoryInGb
            }
          }
          volumeMounts: [
            {
              name: 'leandata'
              mountPath: '/Data'
              readOnly: false
            }
            {
              name: 'leanresults'
              mountPath: '/Results'
              readOnly: false
            }
          ]
        }
      }
    ]
    osType: 'Linux'
    restartPolicy: restartPolicy
    imageRegistryCredentials: [
      {
        server: containerRegistryServer
        username: containerRegistryUsername
        password: containerRegistryPassword
      }
    ]
    volumes: [
      {
        name: 'leandata'
        azureFile: {
          shareName: fileShareName
          storageAccountName: storageAccountName
          storageAccountKey: storageAccount.listKeys().keys[0].value
        }
      }
      {
        name: 'leanresults'
        azureFile: {
          shareName: 'results'
          storageAccountName: storageAccountName
          storageAccountKey: storageAccount.listKeys().keys[0].value
        }
      }
    ]
    diagnostics: {
      logAnalytics: {
        workspaceId: logAnalyticsWorkspaceId
        workspaceKey: logAnalyticsWorkspaceKey
      }
    }
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output id string = containerGroup.id
output name string = containerGroup.name
output principalId string = containerGroup.identity.principalId
output fqdn string = containerGroup.properties.?ipAddress.?fqdn ?? ''
output ipAddress string = containerGroup.properties.?ipAddress.?ip ?? ''
