# Lean Trading Engine - Azure Deployment Quick Reference

## Quick Start

### Deploy Everything (All Three Stages)
```powershell
cd C:\GitRepo\Finance\LeanFork
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Location eastus -Stage all
```

### Individual Stages

```powershell
# Stage 1: Deploy Infrastructure
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Location eastus -Stage infrastructure

# Stage 2: Build and Push Docker Image
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.0.0

# Stage 3: Deploy Container
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage container -ImageTag v1.0.0
```

### Monitor Container
```powershell
# View logs
az container logs --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus

# Follow logs (live stream)
az container logs --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus --follow

# Attach to container
az container attach --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus
```

## Resource Names

All resources follow this naming pattern:

```
Environment: dev
Location: eastus (short: eus)

- Resource Group:      rg-lp-lean-dev-eus
- Container Registry:  acrlpleandeveus
- Container Instance:  aci-lp-lean-dev-eus
- Storage Account: stlpleandeveus
- Log Analytics:       log-lp-lean-dev-eus
- Key Vault:          kv-lp-lean-dev-eus
```

## Common Commands

### Container Management
```powershell
# Restart container
az container restart --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus

# Stop container
az container stop --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus

# Start container
az container start --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus

# Show container details
az container show --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus
```

### Docker Image Management
```powershell
# Build and push new image using staged deployment
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.1.0

# List images in ACR
az acr repository list --name acrlpleandeveus

# List tags for an image
az acr repository show-tags --name acrlpleandeveus --repository lean-custom

# Delete an image tag
az acr repository delete --name acrlpleandeveus --image lean-custom:old-tag --yes

# Note: The deployment uses DockerfileNew for building images
```

### Storage Management
```powershell
# List file shares
az storage share list --account-name stlpleandeveus

# Download results from file share
az storage file download-batch --account-name stlpleandeveus --source results --destination ./local-results

# Upload data to file share
az storage file upload-batch --account-name stlpleandeveus --destination fs-lp-lean-data --source ./local-data
```

### Log Analytics Queries
```powershell
# Run a query
az monitor log-analytics query -w <workspace-id> --analytics-query "ContainerInstanceLog_CL | where TimeGenerated > ago(1h) | order by TimeGenerated desc"
```

### Key Vault Secrets
```powershell
# List secrets
az keyvault secret list --vault-name kv-lp-lean-dev-eus

# Set a secret
az keyvault secret set --vault-name kv-lp-lean-dev-eus --name "api-access-token" --value "your-token"

# Get a secret value
az keyvault secret show --vault-name kv-lp-lean-dev-eus --name "api-access-token" --query value -o tsv
```

## Deployment Environments

### Development
- CPU: 1 vCPU
- Memory: 2 GB
- Log Retention: 30 days
- ACR SKU: Basic
- Algorithm: ExperimentalAlgorithm

### Staging
- CPU: 2 vCPU
- Memory: 3 GB
- Log Retention: 30 days
- ACR SKU: Standard
- Algorithm: ExperimentalAlgorithm

### Production
- CPU: 2 vCPU
- Memory: 4 GB
- Log Retention: 90 days
- ACR SKU: Standard
- Algorithm: MomentumBasedFrameworkAlgorithm

## Troubleshooting

### Container won't start
```powershell
# Check container events
az container show --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus --query instanceView.events

# Check container state
az container show --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus --query instanceView.state
```

### Can't pull image from ACR
```powershell
# Login to ACR
az acr login --name acrlpleandeveus

# Verify image exists
az acr repository show --name acrlpleandeveus --repository lean-custom

# Check ACR credentials
az acr credential show --name acrlpleandeveus
```

### Storage mount issues
```powershell
# Check storage account
az storage account show --name stlpleandeveus --query provisioningState

# Verify file share
az storage share exists --account-name stlpleandeveus --name fs-lp-lean-data

# Get storage account keys
az storage account keys list --resource-group rg-lp-lean-dev-eus --account-name stlpleandeveus
```

## Cost Management

### View costs by resource group
```powershell
az consumption usage list --start-date 2024-01-01 --end-date 2024-01-31 | ConvertFrom-Json | Where-Object {$_.instanceName -like "*lp-lean*"}
```

### Stop container to save costs
```powershell
# Stop container (you pay only for storage)
az container stop --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus

# Delete container instance (keeps other resources)
az container delete --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus --yes
```

## Cleanup

### Remove specific environment
```powershell
.\cleanup.ps1 -Environment dev
```

### Remove with force (no confirmation)
```powershell
.\cleanup.ps1 -Environment dev -Force
```

## Update Configuration

### Update algorithm
Edit the parameter file (`parameters.dev.json`):
```json
"algorithmTypeName": {
  "value": "MomentumBasedAlgorithm"
}
```

Then redeploy the container:
```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage container
```

### Scale container resources
Edit the parameter file (`parameters.dev.json`):
```json
"containerCpu": {
  "value": 2
},
"containerMemoryInGb": {
  "value": 4
}
```

Then redeploy the container:
```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage container
```

## Portal Links

### View resources
- Resource Group: `https://portal.azure.com/#@/resource/subscriptions/{subscription-id}/resourceGroups/rg-lp-lean-{env}-{location}`
- Container Instance: `https://portal.azure.com/#@/resource/subscriptions/{subscription-id}/resourceGroups/rg-lp-lean-{env}-{location}/providers/Microsoft.ContainerInstance/containerGroups/aci-lp-lean-{env}-{location}`
- Log Analytics: `https://portal.azure.com/#@/resource/subscriptions/{subscription-id}/resourceGroups/rg-lp-lean-{env}-{location}/providers/Microsoft.OperationalInsights/workspaces/log-lp-lean-{env}-{location}/logs`

## Support

- Azure Container Instances: https://learn.microsoft.com/azure/container-instances/
- Azure Container Registry: https://learn.microsoft.com/azure/container-registry/
- Bicep Language: https://learn.microsoft.com/azure/azure-resource-manager/bicep/
