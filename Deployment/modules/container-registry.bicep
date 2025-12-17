// ==============================================================================
// Azure Container Registry Module
// ==============================================================================

@description('The name of the container registry')
param name string

@description('The location for the container registry')
param location string

@description('Tags to apply to the resource')
param tags object

@description('The SKU of the container registry')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param sku string = 'Basic'

@description('Enable admin user')
param adminUserEnabled bool = true

@description('Log Analytics workspace ID for diagnostics')
param logAnalyticsWorkspaceId string = ''

// ==============================================================================
// Resources
// ==============================================================================

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2025-05-01-preview' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: adminUserEnabled
    publicNetworkAccess: 'Enabled'
    networkRuleBypassOptions: 'AzureServices'
    policies: {
      retentionPolicy: {
   status: 'disabled'
        days: 7
      }
      trustPolicy: {
    status: 'disabled'
      }
    }
  }
}

// Diagnostic settings
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = if (!empty(logAnalyticsWorkspaceId)) {
  scope: containerRegistry
  name: '${name}-diagnostics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
    {
 category: 'ContainerRegistryRepositoryEvents'
        enabled: true
      }
      {
        category: 'ContainerRegistryLoginEvents'
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

output id string = containerRegistry.id
output name string = containerRegistry.name
output loginServer string = containerRegistry.properties.loginServer
output username string = adminUserEnabled ? containerRegistry.listCredentials().username : ''
output password string = adminUserEnabled ? containerRegistry.listCredentials().passwords[0].value : ''
