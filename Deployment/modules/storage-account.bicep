// ==============================================================================
// Storage Account Module
// ==============================================================================

@description('The name of the storage account')
param name string

@description('The location for the storage account')
param location string

@description('Tags to apply to the resource')
param tags object

@description('The name of the file share')
param fileShareName string = 'leandata'

@description('File share quota in GB')
param fileShareQuota int = 100

@description('Log Analytics workspace ID for diagnostics')
param logAnalyticsWorkspaceId string = ''

// ==============================================================================
// Resources
// ==============================================================================

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true
    azureFilesIdentityBasedAuthentication: {
      directoryServiceOptions: 'None'
    }
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
    encryption: {
      services: {
        file: {
          enabled: true
        }
        blob: {
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
  }
}

// File Services
resource fileServices 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    shareDeleteRetentionPolicy: {
    enabled: true
  days: 7
    }
  }
}

// Data file share
resource fileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  parent: fileServices
  name: fileShareName
  properties: {
  shareQuota: fileShareQuota
    enabledProtocols: 'SMB'
  }
}

// Results file share
resource resultsFileShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  parent: fileServices
  name: 'results'
  properties: {
    shareQuota: 50
    enabledProtocols: 'SMB'
  }
}

// Diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  scope: storageAccount
  name: '${name}-diagnostics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [
  {
        category: 'Transaction'
   enabled: true
      }
    ]
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output id string = storageAccount.id
output name string = storageAccount.name
output fileShareName string = fileShare.name
