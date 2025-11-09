# Staged Deployment Guide

This guide explains the three-stage deployment process for the Lean Trading Engine on Azure.

## Overview

The deployment process is split into three stages to handle the dependency between infrastructure and Docker images:

1. **Stage 1: Infrastructure** - Deploy ACR, Storage, Key Vault, Log Analytics
2. **Stage 2: Image Build** - Build and push Docker image to ACR
3. **Stage 3: Container Instance** - Deploy the container instance with the image

## Prerequisites

- **Azure CLI** 2.61.0 or later
- **Bicep CLI** (auto-installed with Azure CLI)
- **Docker Desktop** (for image build stage)
- **PowerShell** 5.1 or later (PowerShell 7+ recommended)
- **Azure Subscription** with appropriate permissions

## Quick Start

### Deploy Everything (All Stages)

```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Location eastus -Stage all
```

This will:
1. Deploy all infrastructure resources
2. Build and push the Docker image
3. Deploy the container instance

## Stage-by-Stage Deployment

### Stage 1: Deploy Infrastructure

Deploy the base infrastructure (ACR, Storage, Key Vault, Log Analytics):

```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Location eastus -Stage infrastructure
```

**Outputs saved to:** `Deployment\outputs\dev-infra-outputs.json`

**Resources created:**
- Resource Group: `rg-lp-lean-dev-eus`
- Container Registry: `acrlpleandeveus`
- Storage Account: `stlpleandeveus`
- Log Analytics: `log-lp-lean-dev-eus`
- Key Vault: `kv-lp-lean-dev-eus`

### Stage 2: Build and Push Image

Build the Docker image and push it to ACR:

```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.0.0
```

**Requirements:**
- Stage 1 must be completed first
- Docker Desktop must be running
- `DockerfileNew` must exist in the root directory

**What happens:**
1. Logs into Azure Container Registry using admin credentials
2. Builds Docker image from `DockerfileNew`
3. Tags image as `acrlpleandeveus.azurecr.io/lean-custom:v1.0.0`
4. Pushes image to ACR

### Stage 3: Deploy Container Instance

Deploy the container instance using the pushed image:

```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage container -ImageTag v1.0.0
```

**Requirements:**
- Stage 1 and 2 must be completed first
- Image must exist in ACR with the specified tag

**Outputs saved to:** `Deployment\outputs\dev-container-outputs.json`

**Resources created:**
- Container Instance: `aci-lp-lean-dev-eus`
- Role Assignments: AcrPull, Storage, Key Vault access

## Command Reference

### Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `-Environment` | Yes | - | Environment name: `dev`, `staging`, or `prod` |
| `-Location` | No | `eastus` | Azure region for deployment |
| `-SubscriptionId` | No | Default sub | Azure subscription ID |
| `-Stage` | No | `all` | Stage to execute: `infrastructure`, `image`, `container`, or `all` |
| `-ImageTag` | No | `latest` | Docker image tag |
| `-WhatIf` | No | `false` | Validate without deploying (infra/container only) |

### Examples

**Deploy to production with specific image version:**
```powershell
.\Deployment\scripts\deploy-staged.ps1 `
    -Environment prod `
    -Location westeurope `
    -Stage all `
    -ImageTag v2.1.0 `
    -SubscriptionId "8ed0499b-aa94-493a-a3e6-f69a9bb7e520"
```

**Validate infrastructure deployment:**
```powershell
.\Deployment\scripts\deploy-staged.ps1 `
    -Environment dev `
    -Stage infrastructure `
    -WhatIf
```

**Rebuild and push new image version:**
```powershell
.\Deployment\scripts\deploy-staged.ps1 `
    -Environment dev `
    -Stage image `
    -ImageTag v1.1.0
```

**Update container with new image:**
```powershell
.\Deployment\scripts\deploy-staged.ps1 `
    -Environment dev `
    -Stage container `
    -ImageTag v1.1.0
```

## Monitoring and Troubleshooting

### View Container Logs

```powershell
az container logs --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus --follow
```

### Check Container Status

```powershell
az container show --resource-group rg-lp-lean-dev-eus --name aci-lp-lean-dev-eus --query "containers[0].instanceView.currentState"
```

### List Images in ACR

```powershell
az acr repository show-tags --name acrlpleandeveus --repository lean-custom
```

### Access Container Instance in Portal

```
https://portal.azure.com/#@/resource/subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.ContainerInstance/containerGroups/{container-name}
```

## File Structure

```
Deployment/
├── main-infrastructure.bicep           # Stage 1: Infrastructure template
├── main-container.bicep                # Stage 3: Container template
├── scripts/
│   └── deploy-staged.ps1              # Three-stage deployment script
├── parameters/
│   ├── parameters.infra.dev.json      # Stage 1: Infrastructure params
│   ├── parameters.infra.staging.json
│   ├── parameters.infra.prod.json
│   ├── parameters.dev.json            # Stage 3: Container params
│   ├── parameters.staging.json
│   └── parameters.prod.json
├── modules/
│   ├── container-instance.bicep
│   ├── container-registry.bicep
│   ├── storage-account.bicep
│   ├── log-analytics.bicep
│   ├── key-vault.bicep
│   └── role-assignments.bicep
└── outputs/
    ├── dev-infra-outputs.json         # Infrastructure deployment outputs
    └── dev-container-outputs.json     # Container deployment outputs
```

## Output Files

### Infrastructure Outputs (`dev-infra-outputs.json`)

Contains:
- Resource group name
- Container registry login server, username, password
- Storage account name and file share name
- Log Analytics workspace ID and key
- Key Vault name and URI

### Container Outputs (`dev-container-outputs.json`)

Contains:
- Container instance name
- Container instance FQDN
- Container instance managed identity principal ID

## Security Notes

1. **ACR Credentials**: Admin username and password are stored in outputs files. Keep these secure.
2. **Managed Identity**: Container instance uses system-assigned managed identity for Key Vault and Storage access.
3. **Role Assignments**: The following roles are automatically assigned:
   - AcrPull: For pulling images from ACR
   - Storage File Data SMB Share Contributor: For mounting file shares
   - Key Vault Secrets User: For reading secrets

## Troubleshooting

### "Infrastructure outputs file not found"
**Solution:** Run Stage 1 (infrastructure) first before attempting Stage 2 or 3.

### "Docker build failed"
**Possible causes:**
- Docker Desktop is not running
- `DockerfileNew` has syntax errors or is missing
- Build context is missing required files (e.g., Launcher/bin/Debug/)

### "Image not found in ACR"
**Solution:** Run Stage 2 (image) to build and push the image before Stage 3.

### "Failed to login to Azure Container Registry"
**Possible causes:**
- ACR admin user is not enabled
- Credentials in outputs file are incorrect
- Network connectivity issues
