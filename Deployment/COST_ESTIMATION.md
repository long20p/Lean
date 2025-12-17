# Azure Deployment - Cost Estimation

## Overview

This document provides cost estimates for running the Lean Trading Engine on Azure Container Instances across different environments.

**Note:** Prices are approximate and based on East US region pricing as of 2024. Actual costs may vary based on usage patterns, data transfer, and Azure pricing changes.

**Deployment Model:** Costs are based on the three-stage deployment approach where infrastructure is deployed separately from the container instance, allowing for flexible scaling and cost optimization.

## Monthly Cost Breakdown

### Development Environment

| Service | Configuration | Est. Monthly Cost |
|---------|--------------|-------------------|
| **Container Instance** | 1 vCPU, 2 GB RAM, 730 hrs/month | $36.00 |
| **Container Registry** | Basic SKU, 10 GB storage | $5.00 |
| **Storage Account** | LRS, 100 GB, minimal transactions | $2.00 |
| **Log Analytics** | 5 GB ingestion/month | $11.50 |
| **Key Vault** | Standard, 1000 operations | $0.15 |
| **Data Transfer** | Minimal outbound (1 GB) | $0.10 |
| **TOTAL** | | **~$54.75/month** |

### Staging Environment

| Service | Configuration | Est. Monthly Cost |
|---------|--------------|-------------------|
| **Container Instance** | 2 vCPU, 3 GB RAM, 730 hrs/month | $81.00 |
| **Container Registry** | Standard SKU, 20 GB storage | $20.00 |
| **Storage Account** | LRS, 100 GB, moderate transactions | $3.00 |
| **Log Analytics** | 10 GB ingestion/month | $23.00 |
| **Key Vault** | Standard, 2000 operations | $0.30 |
| **Data Transfer** | Moderate outbound (5 GB) | $0.50 |
| **TOTAL** | | **~$127.80/month** |

### Production Environment

| Service | Configuration | Est. Monthly Cost |
|---------|--------------|-------------------|
| **Container Instance** | 2 vCPU, 4 GB RAM, 730 hrs/month | $108.00 |
| **Container Registry** | Standard SKU, 50 GB storage | $20.00 |
| **Storage Account** | LRS, 200 GB, high transactions | $5.00 |
| **Log Analytics** | 30 GB ingestion/month, 90-day retention | $69.00 |
| **Key Vault** | Standard, 5000 operations | $0.75 |
| **Data Transfer** | High outbound (20 GB) | $2.00 |
| **Azure Monitor Alerts** | 5 alert rules | $0.50 |
| **TOTAL** | | **~$205.25/month** |

## Detailed Service Pricing

### Azure Container Instances

Pricing per vCPU-hour and GB-hour:
- **vCPU**: $0.0000125 per second ($0.045 per hour)
- **Memory**: $0.0000014 per GB-second ($0.005 per GB-hour)

**Example Calculations:**

**Development (1 vCPU, 2 GB, 730 hours/month):**
- CPU: 1 vCPU × $0.045/hour × 730 hours = $32.85
- Memory: 2 GB × $0.005/GB-hour × 730 hours = $7.30
- **Total**: $40.15/month

**Production (2 vCPU, 4 GB, 730 hours/month):**
- CPU: 2 vCPU × $0.045/hour × 730 hours = $65.70
- Memory: 4 GB × $0.005/GB-hour × 730 hours = $14.60
- **Total**: $80.30/month

### Azure Container Registry

| SKU | Storage | Webhooks | Build Time | Price/Month |
|-----|---------|----------|------------|-------------|
| Basic | 10 GB | 2 | N/A | $5.00 |
| Standard | 100 GB | 10 | N/A | $20.00 |
| Premium | 500 GB | 100 | Yes | $167.00 |

**Additional Costs:**
- Storage over included: $0.10 per GB/month
- Geo-replication (Premium only): $167/month per additional region

### Storage Account

**File Storage Pricing:**
- First 50 TB: $0.06 per GB/month
- Hot tier data: $0.0184 per GB/month
- Transactions: $0.004 per 10,000 operations

**Example (100 GB, 1M transactions/month):**
- Storage: 100 GB × $0.06 = $6.00
- Transactions: 1,000,000 / 10,000 × $0.004 = $0.40
- **Total**: $6.40/month

### Log Analytics Workspace

**Data Ingestion:**
- First 5 GB/day: Free
- Over 5 GB/day: $2.30 per GB

**Data Retention:**
- First 31 days: Free
- Days 31-90: $0.12 per GB/month
- Over 90 days: $0.05 per GB/month

**Example (10 GB ingestion/month, 90-day retention):**
- Ingestion: 10 GB × $2.30 = $23.00
- Retention (60 days): 600 GB × $0.12 = $72.00
- **Total**: $95.00/month

### Key Vault

**Standard Tier:**
- Operations: $0.03 per 10,000 operations
- Secrets: Free
- Keys: $1.00 per key per month (if using customer-managed keys)

**Example (5,000 operations/month):**
- Operations: 5,000 / 10,000 × $0.03 = $0.015
- **Total**: $0.02/month (rounded)

## Cost Optimization Strategies

### 1. Container Instance Optimization

**Schedule-Based Scaling:**
Run containers only during market hours:

```powershell
# Stop container after market close
az container stop --resource-group rg-lp-lean-prod-eus --name aci-lp-lean-prod-eus

# Start container before market open
az container start --resource-group rg-lp-lean-prod-eus --name aci-lp-lean-prod-eus
```

**Savings:** ~67% (running 8 hours/day instead of 24 hours/day)

**Market Hours Example (8 hours/day, 22 days/month):**
- Running time: 8 × 22 = 176 hours/month
- Cost: 2 vCPU × $0.045 × 176 + 4 GB × $0.005 × 176
- **Savings**: ~$252/month → ~$83/month (**$169 saved**)

### 2. Storage Optimization

**Lifecycle Management:**
```powershell
# Move old backlog data to cool tier after 30 days
az storage blob set-tier --account-name stlpleanprodeus --container-name archive --name old-data/* --tier Cool
```

**Savings:** Cool storage is $0.01/GB vs $0.0184/GB hot
- 50 GB moved to cool: (0.0184 - 0.01) × 50 = **$0.42/month saved**

### 3. Log Analytics Optimization

**Data Retention:**
```bicep
retentionInDays: 30  // Instead of 90 for non-production
```

**Savings for Dev/Staging:**
- 60 days × 10 GB × $0.12 = $72/month saved

**Filter Unnecessary Logs:**
```kql
// Only ingest errors and warnings, not info logs
ContainerInstanceLog_CL
| where Level in ("Error", "Warning")
```

### 4. Container Registry Optimization

**Image Cleanup Policy:**
```powershell
# Delete untagged images
az acr repository show-manifests --name acrlpleanprodeus --repository lean-custom --query "[?tags[0]==null].digest" -o tsv | `
    ForEach-Object { az acr repository delete --name acrlpleanprodeus --image "lean-custom@$_" --yes }

# Keep only last 10 tags
az acr repository show-tags --name acrlpleanprodeus --repository lean-custom --orderby time_desc --top 10
```

**Savings:** Reduce storage from 50 GB to 20 GB = **$3/month saved**

### 5. Network Optimization

**Minimize Data Transfer:**
- Use Azure File Share mount instead of downloading data
- Cache frequently accessed data
- Compress backtest results

**Savings:** Reduce outbound transfer from 20 GB to 5 GB
- (20 - 5) × $0.10 = **$1.50/month saved**

## Cost Monitoring

### Set Up Budget Alerts

```powershell
# Create a budget for the resource group
az consumption budget create `
    --budget-name "lean-prod-monthly-budget" `
    --category Cost `
  --amount 250 `
    --time-period-start "2024-01-01" `
    --time-period-end "2024-12-31" `
    --time-grain Monthly `
    --resource-group rg-lp-lean-prod-eus
```

### Query Costs

```powershell
# Get costs by service
az consumption usage list `
    --start-date 2024-01-01 `
    --end-date 2024-01-31 `
    --query "[?contains(instanceName, 'lp-lean')]" `
    --output table

# Export to CSV
az consumption usage list `
    --start-date 2024-01-01 `
    --end-date 2024-01-31 `
    --query "[?contains(instanceName, 'lp-lean')].{Service:meterCategory, Instance:instanceName, Cost:pretaxCost, Unit:unit}" `
    --output json | ConvertFrom-Json | Export-Csv -Path "costs.csv"
```

### Azure Cost Management

Use Azure Cost Management + Billing:
1. Navigate to Cost Management in Azure Portal
2. Create cost analysis views
3. Set up anomaly alerts
4. Configure budget alerts

## Estimated Annual Costs

| Environment | Monthly | Annual | With Optimization | Annual Savings |
|-------------|---------|--------|-------------------|----------------|
| Development | $54.75 | $657 | $35 | $237 (36%) |
| Staging | $127.80 | $1,534 | $90 | $454 (30%) |
| Production | $205.25 | $2,463 | $140 | $782 (32%) |
| **TOTAL** | **$387.80** | **$4,654** | **$265** | **$1,473 (32%)** |

## Alternative Architectures

### Option 1: Azure Container Apps (Serverless)

**Pros:**
- Scales to zero when not in use
- Built-in ingress and HTTPS
- Automatic revisions

**Cons:**
- More complex setup
- May have cold start delays

**Estimated Cost:**
- $0 when scaled to zero
- ~$0.000024 per vCPU-second when running
- **Potential savings: 50-70% for intermittent workloads**

### Option 2: Azure Kubernetes Service (AKS)

**Pros:**
- Full Kubernetes features
- Better for multiple algorithms
- Auto-scaling capabilities

**Cons:**
- More expensive (~$70/month for control plane)
- Requires more management
- Overkill for single container

**Estimated Cost:**
- Control plane: $70/month
- Nodes: $50-200/month
- **Total: $120-270/month minimum**

### Option 3: Azure App Service (Container)

**Pros:**
- Easy deployment
- Built-in monitoring
- Better for web-based algorithms

**Cons:**
- Less control over container
- Higher baseline cost

**Estimated Cost:**
- Basic tier (B1): $55/month
- Standard tier (S1): $70/month

## Recommendations

### Development
- Use smallest container size (1 vCPU, 1.5 GB)
- 30-day log retention
- Basic ACR tier
- Stop container when not actively developing
- **Target: $35/month**

### Staging
- Match production configuration
- Test during business hours only
- Share ACR with production
- **Target: $90/month**

### Production
- Right-size based on actual algorithm needs
- Implement scheduled stop/start if only trading during market hours
- Use 90-day log retention for compliance
- Implement automated cost alerts
- **Target: $140/month with optimization**

## Cost Tracking Template

Use this template to track monthly costs:

```
Month: January 2024
Environment: Production

Service              | Budget  | Actual  | Variance | Notes
---------------------------|---------|---------|----------|------------------
Container Instance         | $108.00 | $95.23  | -$12.77  | Stopped on weekends
Container Registry         | $20.00  | $20.00  | $0.00    |
Storage Account            | $5.00   | $6.45   | +$1.45   | Higher transactions
Log Analytics        | $69.00  | $73.20  | +$4.20   | More verbose logging
Key Vault        | $0.75   | $0.80   | +$0.05   |
Data Transfer  | $2.00 | $1.50   | -$0.50   | Less data downloaded
Alerts    | $0.50   | $0.50   | $0.00    |
---------------------------|---------|---------|----------|------------------
TOTAL       | $205.25 | $197.68 | -$7.57   |

Optimization Actions:
- Reduced container runtime by 15% through scheduled stops
- Increased logging temporarily for debugging (will reduce next month)
```

## Support Resources

- [Azure Pricing Calculator](https://azure.microsoft.com/pricing/calculator/)
- [Azure Cost Management](https://azure.microsoft.com/services/cost-management/)
- [Container Instances Pricing](https://azure.microsoft.com/pricing/details/container-instances/)
- [Storage Pricing](https://azure.microsoft.com/pricing/details/storage/)
