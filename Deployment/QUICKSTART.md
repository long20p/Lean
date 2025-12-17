# Quick Start: Staged Deployment

## Complete Deployment (All Three Stages)

```powershell
cd C:\GitRepo\Finance\LeanFork
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Location westeurope -Stage all
```

---

## Individual Stages

### 1. Deploy Infrastructure Only
```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Location westeurope -Stage infrastructure
```

### 2. Build & Push Image Only
```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.0.0
```

### 3. Deploy Container Only
```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage container -ImageTag v1.0.0
```

---

## Common Commands

### Validate Before Deploy
```powershell
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage infrastructure -WhatIf
```

### Deploy with Specific Subscription
```powershell
.\Deployment\scripts\deploy-staged.ps1 `
    -Environment dev `
    -Location westeurope `
    -Stage all `
    -SubscriptionId "8ed0499b-aa94-493a-a3e6-f69a9bb7e520"
```

### Redeploy Container with New Image
```powershell
# First, build and push new image
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.1.0

# Then, update container
.\Deployment\scripts\deploy-staged.ps1 -Environment dev -Stage container -ImageTag v1.1.0
```

---

## Monitoring

### View Container Logs
```powershell
az container logs --resource-group rg-lp-lean-dev-weu --name aci-lp-lean-dev-weu --follow
```

### Check Container Status
```powershell
az container show --resource-group rg-lp-lean-dev-weu --name aci-lp-lean-dev-weu
```

### List ACR Images
```powershell
az acr repository show-tags --name acrlpleandevweu --repository lean-custom
```

---

## Output Files

- **Infrastructure outputs**: `Deployment\outputs\dev-infra-outputs.json`
- **Container outputs**: `Deployment\outputs\dev-container-outputs.json`

---

## Troubleshooting

**Error: "Infrastructure outputs file not found"**
→ Run Stage 1 (infrastructure) first

**Error: "Image not found"**
→ Run Stage 2 (image) to build and push

**Error: "Docker daemon not running"**
→ Start Docker Desktop
