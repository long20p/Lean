# Lean Trading Engine - Azure Deployment

This directory contains Infrastructure as Code (IaC) files for deploying the QuantConnect Lean trading engine to Azure Container Instances.

## Prerequisites

- **Azure CLI** 2.61.0 or later
- **Bicep CLI** (auto-installed with Azure CLI)
- **Docker Desktop** (for image build stage)
- **PowerShell** 5.1 or later (PowerShell 7+ recommended)
- **Azure Subscription** with appropriate permissions

## Resource Naming Convention

All resources follow Azure naming conventions with the "lp" identifier:

| Resource Type | Naming Pattern | Example |
|--------------|----------------|---------|
| Resource Group | `rg-lp-lean-{env}-{region}` | `rg-lp-lean-prod-eastus` |
| Container Registry | `acrlplean{env}{region}` | `acrlpleanprodeastus` |
| Container Instance | `aci-lp-lean-{env}-{region}` | `aci-lp-lean-prod-eastus` |
| Storage Account | `stlplean{env}{region}` | `stlpleanprodeastus` |
| File Share | `fs-lp-lean-data` | `fs-lp-lean-data` |
| Log Analytics | `log-lp-lean-{env}-{region}` | `log-lp-lean-prod-eastus` |
| Key Vault | `kv-lp-lean-{env}-{region}` | `kv-lp-lean-prod-eastus` |

## Architecture

The deployment creates the following Azure resources:

1. **Resource Group**: Container for all resources
2. **Azure Container Registry**: Stores the Lean Docker image
3. **Azure Container Instance**: Runs the Lean trading engine
4. **Storage Account with File Share**: Persists algorithm data and results
5. **Log Analytics Workspace**: Centralized logging and monitoring
6. **Key Vault**: Secure storage for API keys and secrets

## Directory Structure

```
Deployment/
├── README.md                           # This file
├── DEPLOYMENT-GUIDE.md                 # Detailed deployment guide
├── QUICKSTART.md                       # Quick start commands
├── SECURITY.md                         # Security best practices
├── COST_ESTIMATION.md                  # Cost breakdown
├── main-infrastructure.bicep           # Stage 1: Infrastructure template
├── main-container.bicep                # Stage 3: Container template
├── modules/
│   ├── container-registry.bicep        # Azure Container Registry module
│   ├── container-instance.bicep        # Azure Container Instance module
│   ├── storage-account.bicep           # Storage Account module
│   ├── log-analytics.bicep             # Log Analytics module
│   ├── key-vault.bicep                 # Key Vault module
│   └── role-assignments.bicep          # RBAC role assignments module
├── parameters/
│   ├── parameters.infra.dev.json       # Stage 1: Infrastructure params (dev)
│   ├── parameters.infra.staging.json   # Stage 1: Infrastructure params (staging)
│   ├── parameters.infra.prod.json      # Stage 1: Infrastructure params (prod)
│   ├── parameters.dev.json             # Stage 3: Container params (dev)
│   ├── parameters.staging.json         # Stage 3: Container params (staging)
│   └── parameters.prod.json            # Stage 3: Container params (prod)
├── scripts/
│   └── deploy-staged.ps1               # Three-stage deployment script
└── outputs/
    ├── dev-infra-outputs.json          # Stage 1 outputs
    └── dev-container-outputs.json      # Stage 3 outputs
```

## Deployment Steps

### Quick Start - All Three Stages

```powershell
# Login to Azure
az login

# Deploy everything (infrastructure, build image, deploy container)
.\scripts\deploy-staged.ps1 -Environment dev -Location eastus -Stage all
```

### Stage-by-Stage Deployment

#### Stage 1: Deploy Infrastructure

Deploy base resources (ACR, Storage, Key Vault, Log Analytics):

```powershell
.\scripts\deploy-staged.ps1 -Environment dev -Location eastus -Stage infrastructure
```

#### Stage 2: Build and Push Docker Image

Build the Lean Docker image and push to ACR:

```powershell
.\scripts\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.0.0
```

#### Stage 3: Deploy Container Instance

Deploy the container with the pushed image:

```powershell
.\scripts\deploy-staged.ps1 -Environment dev -Stage container -ImageTag v1.0.0
```

### 3. Verify Deployment

Check the container logs:

```powershell
# Get container logs
az container logs --resource-group rg-lp-lean-dev-eastus --name aci-lp-lean-dev-eastus

# Stream container logs
az container attach --resource-group rg-lp-lean-dev-eastus --name aci-lp-lean-dev-eastus
```

## Configuration

### Environment Variables

The container instance is configured with the following environment variables:

- `ENVIRONMENT`: Deployment environment (dev/staging/prod)
- `CONFIG_FILE`: Path to configuration file (default: config.json)
- `RESULTS_PATH`: Path to results directory (default: /Results)

### Secrets

Sensitive configuration values are stored in Azure Key Vault:

- API access tokens
- Brokerage credentials
- Data provider API keys

Reference secrets in the container using environment variables:

```bicep
environmentVariables: [
  {
    name: 'API_ACCESS_TOKEN'
    secureValue: keyVaultReference.getSecret('api-access-token')
  }
]
```

## Monitoring

### Log Analytics

All container logs are sent to Log Analytics workspace. Query logs using KQL:

```kql
ContainerInstanceLog_CL
| where ContainerGroup_s == "aci-lp-lean-prod-eastus"
| order by TimeGenerated desc
| take 100
```

### Metrics

Monitor container metrics in Azure Portal:

- CPU usage
- Memory usage
- Network I/O
- Container restart count

## Cost Optimization

- **Development**: Use smaller container instances (1 vCPU, 1.5 GB memory)
- **Production**: Scale based on workload requirements
- **Scheduled Jobs**: Consider using Azure Container Instances with scheduled start/stop
- **Storage**: Use locally-redundant storage (LRS) for non-critical data

## Security Best Practices

1. Store all secrets in Azure Key Vault
2. Use managed identities for authentication
3. Enable Azure Container Registry authentication
4. Restrict network access using firewall rules
5. Enable diagnostic logging for all resources

## Troubleshooting

### Container fails to start

```powershell
# Check container events
az container show --resource-group rg-lp-lean-dev-eastus --name aci-lp-lean-dev-eastus --query instanceView.events

# Check container logs
az container logs --resource-group rg-lp-lean-dev-eastus --name aci-lp-lean-dev-eastus
```

### Unable to pull image from ACR

```powershell
# Verify ACR authentication
az acr login --name acrlpleandeveastus

# Check ACR repository
az acr repository list --name acrlpleandeveastus
```

### Storage mount issues

```powershell
# Verify storage account key
az storage account keys list --resource-group rg-lp-lean-dev-eastus --account-name stlpleandeveastus

# Check file share exists
az storage share list --account-name stlpleandeveastus
```

## Cleanup

To remove all deployed resources:

```powershell
.\scripts\cleanup.ps1 -Environment dev -Location eastus
```

## Support

For issues and questions:
- Open an issue in the repository
- Review Azure Container Instances documentation
- Check QuantConnect Lean documentation
