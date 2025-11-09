// ==============================================================================
// Log Analytics Workspace Module
// ==============================================================================

@description('The name of the Log Analytics workspace')
param name string

@description('The location for the workspace')
param location string

@description('Tags to apply to the resource')
param tags object

@description('Data retention in days')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Workspace SKU')
@allowed([
  'PerGB2018'
  'Free'
  'Standalone'
  'PerNode'
  'Standard'
  'Premium'
])
param sku string = 'PerGB2018'

// ==============================================================================
// Resources
// ==============================================================================

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    sku: {
      name: sku
    }
    retentionInDays: retentionInDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
      disableLocalAuth: false  // Cannot disable yet due to ACI requirement
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Container Insights Solution
resource containerInsights 'Microsoft.OperationsManagement/solutions@2015-11-01-preview' = {
  name: 'ContainerInsights(${logAnalyticsWorkspace.name})'
  location: location
  tags: tags
  plan: {
    name: 'ContainerInsights(${logAnalyticsWorkspace.name})'
  product: 'OMSGallery/ContainerInsights'
    promotionCode: ''
    publisher: 'Microsoft'
  }
  properties: {
    workspaceResourceId: logAnalyticsWorkspace.id
  }
}

// ==============================================================================
// Outputs
// ==============================================================================

output id string = logAnalyticsWorkspace.id
output name string = logAnalyticsWorkspace.name
output workspaceId string = logAnalyticsWorkspace.properties.customerId
output workspaceResourceId string = logAnalyticsWorkspace.id
output principalId string = logAnalyticsWorkspace.identity.principalId
output workspaceKey string = logAnalyticsWorkspace.listKeys().primarySharedKey
