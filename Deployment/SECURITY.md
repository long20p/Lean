# Azure Deployment - Security Best Practices

## Overview

This document outlines security best practices for deploying the Lean Trading Engine to Azure Container Instances.

**Note:** The deployment uses a three-stage approach where infrastructure is deployed first, then the Docker image is built and pushed, and finally the container instance is deployed. This ensures proper separation of concerns and better security management.

## Authentication and Authorization

### 1. Azure Service Principal

Create a service principal with minimal required permissions:

```powershell
# Create service principal for CI/CD
az ad sp create-for-rbac `
    --name "sp-lp-lean-cicd" `
    --role "Contributor" `
  --scopes "/subscriptions/{subscription-id}/resourceGroups/rg-lp-lean-*" `
 --sdk-auth

# Store the output as AZURE_CREDENTIALS in GitHub Secrets
```

### 2. Managed Identity

Enable managed identity for Container Instance to access other Azure resources:

```bicep
identity: {
  type: 'SystemAssigned'
}
```

Grant permissions to Key Vault:

```powershell
$principalId = az container show `
    --resource-group rg-lp-lean-prod-eus `
    --name aci-lp-lean-prod-eus `
    --query identity.principalId -o tsv

az keyvault set-policy `
    --name kv-lp-lean-prod-eus `
    --object-id $principalId `
    --secret-permissions get list
```

## Secrets Management

### 1. Azure Key Vault

Store all sensitive data in Key Vault:

```powershell
# API tokens
az keyvault secret set --vault-name kv-lp-lean-prod-eus --name "quantconnect-api-token" --value "your-token"

# Brokerage credentials
az keyvault secret set --vault-name kv-lp-lean-prod-eus --name "ib-username" --value "your-username"
az keyvault secret set --vault-name kv-lp-lean-prod-eus --name "ib-password" --value "your-password"

# Data provider keys
az keyvault secret set --vault-name kv-lp-lean-prod-eus --name "tiingo-auth-token" --value "your-token"
```

### 2. Reference Secrets in Container

Update the container instance to reference Key Vault secrets:

```bicep
environmentVariables: [
  {
    name: 'QUANTCONNECT_API_TOKEN'
    secureValue: reference(resourceId('Microsoft.KeyVault/vaults/secrets', keyVaultName, 'quantconnect-api-token')).secretValue
  }
]
```

### 3. Never Commit Secrets

Add to `.gitignore`:

```gitignore
# Secrets and credentials
*.env
*.secret
*credentials*
appsettings.Production.json
config.production.json

# Deployment outputs containing sensitive data
Deployment/outputs/*.json
```

### 4. Secure Deployment Outputs

The staged deployment creates output files that contain sensitive information:
- `Deployment/outputs/*-infra-outputs.json` - Contains ACR credentials, Log Analytics keys
- `Deployment/outputs/*-container-outputs.json` - Contains container instance details

**Important:**
- These files are stored locally and should **never** be committed to version control
- Add `Deployment/outputs/*.json` to `.gitignore`
- Store sensitive outputs in Azure Key Vault if needed for automation
- Rotate ACR credentials regularly using `az acr credential renew`

## Network Security

### 1. Restrict Container Registry Access

Enable firewall rules for ACR:

```powershell
# Disable public access
az acr update --name acrlpleanprodeus --public-network-enabled false

# Add allowed IP ranges
az acr network-rule add `
    --name acrlpleanprodeus `
    --ip-address 203.0.113.0/24
```

### 2. Private Endpoints

For production, use private endpoints:

```bicep
resource privateEndpoint 'Microsoft.Network/privateEndpoints@2023-04-01' = {
  name: 'pe-${containerRegistryName}'
  location: location
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'acr-connection'
        properties: {
          privateLinkServiceId: containerRegistry.id
       groupIds: ['registry']
        }
      }
  ]
  }
}
```

### 3. Storage Account Firewall

Restrict storage account access:

```powershell
# Update storage account network rules
az storage account update `
    --name stlpleanprodeus `
    --default-action Deny

# Add allowed IP addresses
az storage account network-rule add `
    --account-name stlpleanprodeus `
    --ip-address 203.0.113.10
```

## Data Protection

### 1. Enable Encryption

Storage account encryption (enabled by default):

```bicep
encryption: {
  services: {
    file: {
   enabled: true
      keyType: 'Account'
    }
    blob: {
  enabled: true
      keyType: 'Account'
    }
  }
  keySource: 'Microsoft.Storage'
}
```

### 2. Backup Strategy

Configure backup for critical data:

```powershell
# Create snapshots of file shares
az storage share snapshot `
    --account-name stlpleanprodeus `
    --name fs-lp-lean-data

# Implement automated backup script
$date = Get-Date -Format "yyyyMMdd-HHmmss"
az storage file download-batch `
    --account-name stlpleanprodeus `
    --source fs-lp-lean-data `
    --destination "backups/$date"
```

### 3. Soft Delete

Enable soft delete for recovery:

```powershell
az storage account blob-service-properties update `
    --account-name stlpleanprodeus `
    --enable-delete-retention true `
    --delete-retention-days 30
```

## Monitoring and Auditing

### 1. Enable Diagnostic Logging

All resources should send diagnostics to Log Analytics:

```bicep
resource diagnosticSettings 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'default'
  scope: resource
  properties: {
 workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
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
```

### 2. Set Up Alerts

Create alerts for security events:

```powershell
# Alert on container restart
az monitor metrics alert create `
    --name "Container Restart Alert" `
    --resource-group rg-lp-lean-prod-eus `
    --scopes "/subscriptions/{sub-id}/resourceGroups/rg-lp-lean-prod-eus/providers/Microsoft.ContainerInstance/containerGroups/aci-lp-lean-prod-eus" `
    --condition "count restartCount > 5" `
    --window-size 5m `
    --evaluation-frequency 1m
```

### 3. Key Vault Audit Logs

Query Key Vault access:

```kql
AzureDiagnostics
| where ResourceProvider == "MICROSOFT.KEYVAULT"
| where OperationName == "SecretGet"
| project TimeGenerated, CallerIPAddress, identity_claim_oid_g, ResultSignature
| order by TimeGenerated desc
```

## Compliance

### 1. Azure Policy

Apply policies to ensure compliance:

```powershell
# Require tags on all resources
az policy assignment create `
    --name "require-tags" `
    --policy "96670d01-0a4d-4649-9c89-2d3abc0a5025" `
  --scope "/subscriptions/{subscription-id}/resourceGroups/rg-lp-lean-prod-eus"

# Enforce HTTPS only for storage
az policy assignment create `
    --name "https-storage-only" `
    --policy "404c3081-a854-4457-ae30-26a93ef643f9" `
    --scope "/subscriptions/{subscription-id}/resourceGroups/rg-lp-lean-prod-eus"
```

### 2. RBAC Best Practices

Implement least privilege access:

```powershell
# Developer role - read-only access
az role assignment create `
    --role "Reader" `
    --assignee "user@domain.com" `
    --scope "/subscriptions/{sub-id}/resourceGroups/rg-lp-lean-dev-eus"

# Operations role - can restart containers
az role assignment create `
    --role "Contributor" `
    --assignee "ops@domain.com" `
    --scope "/subscriptions/{sub-id}/resourceGroups/rg-lp-lean-prod-eus/providers/Microsoft.ContainerInstance/containerGroups/aci-lp-lean-prod-eus"
```

## Incident Response

### 1. Container Compromise

If a container is compromised:

```powershell
# Immediately stop the container
az container stop --resource-group rg-lp-lean-prod-eus --name aci-lp-lean-prod-eus

# Export logs for analysis
az container logs --resource-group rg-lp-lean-prod-eus --name aci-lp-lean-prod-eus > incident-logs.txt

# Rotate all secrets
az keyvault secret set-attributes --vault-name kv-lp-lean-prod-eus --name "api-token" --enabled false

# Rebuild and push clean image, then redeploy container
.\Deployment\scripts\deploy-staged.ps1 -Environment prod -Stage image -ImageTag verified-clean
.\Deployment\scripts\deploy-staged.ps1 -Environment prod -Stage container -ImageTag verified-clean

# Review access logs
az monitor activity-log list --resource-group rg-lp-lean-prod-eus --start-time 2024-01-01 --end-time 2024-01-31
```

### 2. Key Vault Access Review

Regularly review Key Vault access:

```powershell
# List Key Vault access policies
az keyvault show --name kv-lp-lean-prod-eus --query properties.accessPolicies

# Review recent secret access
az monitor activity-log list `
    --resource-group rg-lp-lean-prod-eus `
    --namespace Microsoft.KeyVault `
    --start-time (Get-Date).AddDays(-7)
```

## Security Checklist

- [ ] Service principal has minimal required permissions
- [ ] All secrets stored in Key Vault
- [ ] Container uses managed identity
- [ ] Storage account has firewall rules enabled
- [ ] Container Registry has authentication enabled
- [ ] Diagnostic logging enabled for all resources
- [ ] Alerts configured for security events
- [ ] Soft delete enabled on storage
- [ ] Regular backup schedule implemented
- [ ] Azure Policy assignments in place
- [ ] RBAC follows least privilege principle
- [ ] Network access restricted to known IPs
- [ ] Encryption enabled for data at rest
- [ ] TLS 1.2+ enforced for all connections
- [ ] Regular security audits scheduled
- [ ] Incident response plan documented
- [ ] Key rotation schedule defined
- [ ] No secrets in source code or configuration files
- [ ] Container images scanned for vulnerabilities
- [ ] Access reviews performed quarterly

## Vulnerability Scanning

Scan Docker images before deployment:

```powershell
# Install Trivy
choco install trivy

# Scan image
trivy image --severity HIGH,CRITICAL lean-custom:latest

# Scan before pushing to ACR
trivy image --exit-code 1 --severity CRITICAL lean-custom:latest
```

## Regular Maintenance

### Weekly Tasks
- Review container logs for anomalies
- Check resource utilization
- Verify backups completed successfully

### Monthly Tasks
- Rotate Key Vault secrets
- Review access logs
- Update container base images
- Review and update firewall rules

### Quarterly Tasks
- Conduct security audit
- Review and update IAM permissions
- Test disaster recovery procedures
- Update security documentation

## Support and Resources

- [Azure Security Best Practices](https://docs.microsoft.com/azure/security/fundamentals/best-practices-and-patterns)
- [Container Instance Security](https://docs.microsoft.com/azure/container-instances/container-instances-image-security)
- [Key Vault Security](https://docs.microsoft.com/azure/key-vault/general/security-features)
- [Azure Policy Samples](https://docs.microsoft.com/azure/governance/policy/samples/)
