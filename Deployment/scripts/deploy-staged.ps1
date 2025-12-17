<#
.SYNOPSIS
    Three-stage deployment script for Lean Trading Engine to Azure.

.DESCRIPTION
    This script deploys the Lean Trading Engine infrastructure in three stages:
    Stage 1: Deploy infrastructure (ACR, Storage, Key Vault, Log Analytics)
    Stage 2: Build and push Docker image to ACR
    Stage 3: Deploy container instance

.PARAMETER Environment
    The environment to deploy to (dev, staging, prod).

.PARAMETER Location
    The Azure region to deploy to (default: eastus).

.PARAMETER SubscriptionId
    The Azure subscription ID (optional, uses default subscription if not provided).

.PARAMETER Stage
    The deployment stage to execute (infrastructure, image, container, all).
    - infrastructure: Deploy only infrastructure resources
    - image: Build and push Docker image (requires infrastructure stage)
    - container: Deploy container instance (requires image stage)
    - all: Execute all three stages in sequence

.PARAMETER ImageTag
    The Docker image tag (default: latest).

.PARAMETER WhatIf
    Performs a validation-only deployment without making changes (infrastructure and container stages only).

.EXAMPLE
    .\deploy-staged.ps1 -Environment dev -Stage all
    Executes all three stages in sequence.

.EXAMPLE
    .\deploy-staged.ps1 -Environment dev -Stage infrastructure
    Deploys only infrastructure resources.

.EXAMPLE
    .\deploy-staged.ps1 -Environment dev -Stage image -ImageTag v1.0.0
    Builds and pushes image with tag v1.0.0.

.EXAMPLE
    .\deploy-staged.ps1 -Environment dev -Stage container -ImageTag v1.0.0
    Deploys container instance using image tag v1.0.0.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$Location = 'eastus',

    [Parameter(Mandatory = $false)]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $false)]
    [ValidateSet('infrastructure', 'image', 'container', 'all')]
    [string]$Stage = 'all',

    [Parameter(Mandatory = $false)]
    [string]$ImageTag = 'latest',

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf
)

# Set error action preference
$ErrorActionPreference = 'Stop'

# Script variables
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$DeploymentPath = Split-Path -Parent $ScriptPath
$RootPath = Split-Path -Parent $DeploymentPath
$MainBicepInfra = Join-Path $DeploymentPath "main-infrastructure.bicep"
$MainBicepContainer = Join-Path $DeploymentPath "main-container.bicep"
$InfraParametersFile = Join-Path $DeploymentPath "parameters\parameters.infra.$Environment.json"
$ContainerParametersFile = Join-Path $DeploymentPath "parameters\parameters.$Environment.json"
$OutputsDir = Join-Path $DeploymentPath "outputs"
$InfraOutputsFile = Join-Path $OutputsDir "$Environment-infra-outputs.json"
$ContainerOutputsFile = Join-Path $OutputsDir "$Environment-container-outputs.json"

# Ensure outputs directory exists
if (-not (Test-Path $OutputsDir)) {
    New-Item -ItemType Directory -Path $OutputsDir -Force | Out-Null
}

# Banner
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "  Lean Trading Engine - Staged Deployment" -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Environment:  $Environment" -ForegroundColor Yellow
Write-Host "Location:     $Location" -ForegroundColor Yellow
Write-Host "Stage:        $Stage" -ForegroundColor Yellow
Write-Host "Image Tag:    $ImageTag" -ForegroundColor Yellow
Write-Host "What-If:      $WhatIf" -ForegroundColor Yellow
Write-Host ""

# ==============================================================================
# Helper Functions
# ==============================================================================

function Test-Prerequisites {
    Write-Host "[Prerequisites] Checking required tools..." -ForegroundColor Green
    
    # Verify Azure CLI is installed
    $azVersion = az version --output json 2>&1 | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Azure CLI is not installed. Please install from: https://aka.ms/installazurecliwindows"
    }
    Write-Host "✓ Azure CLI version: $($azVersion.'azure-cli')" -ForegroundColor Green
    
    # Verify Bicep is installed
    $bicepVersion = az bicep version
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Installing Bicep..." -ForegroundColor Yellow
        az bicep install
    }
    Write-Host "✓ Bicep version: $bicepVersion" -ForegroundColor Green
    
    # Verify Docker is installed (only if image stage is included)
    if ($Stage -eq 'image' -or $Stage -eq 'all') {
        $dockerVersion = docker --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Docker is not installed. Please install Docker Desktop from: https://www.docker.com/products/docker-desktop"
        }
        Write-Host "✓ Docker version: $dockerVersion" -ForegroundColor Green
    }
    
    Write-Host ""
}

function Connect-AzureAccount {
    Write-Host "[Authentication] Checking Azure authentication..." -ForegroundColor Green
    
    $account = az account show --output json 2>&1 | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Not logged in. Starting authentication..." -ForegroundColor Yellow
        az login
        $account = az account show --output json | ConvertFrom-Json
    }
    Write-Host "✓ Logged in as: $($account.user.name)" -ForegroundColor Green
    
    # Set subscription if provided
    if ($SubscriptionId) {
        az account set --subscription $SubscriptionId
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to set subscription: $SubscriptionId"
        }
        Write-Host "✓ Using subscription: $SubscriptionId" -ForegroundColor Green
    }
    else {
        Write-Host "✓ Using subscription: $($account.name)" -ForegroundColor Green
    }
    
    Write-Host ""
}

function Deploy-Infrastructure {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "  STAGE 1: Infrastructure Deployment" -ForegroundColor Cyan
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Verify files exist
    if (-not (Test-Path $MainBicepInfra)) {
        Write-Error "Infrastructure Bicep file not found: $MainBicepInfra"
    }
    if (-not (Test-Path $InfraParametersFile)) {
        Write-Error "Infrastructure parameters file not found: $InfraParametersFile"
    }
    
    # Generate deployment name
    $DeploymentName = "lean-infra-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    
    Write-Host "Starting infrastructure deployment..." -ForegroundColor Yellow
    Write-Host "Deployment name: $DeploymentName" -ForegroundColor Yellow
    Write-Host ""
    
    if ($WhatIf) {
        Write-Host "Running validation (What-If mode)..." -ForegroundColor Yellow
        az deployment sub what-if `
            --name $DeploymentName `
            --location $Location `
            --template-file $MainBicepInfra `
            --parameters $InfraParametersFile `
            --parameters location=$Location
    }
    else {
        $deployment = az deployment sub create `
            --name $DeploymentName `
            --location $Location `
            --template-file $MainBicepInfra `
            --parameters $InfraParametersFile `
            --parameters location=$Location `
            --output json | ConvertFrom-Json
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Infrastructure deployment failed. Check the error messages above."
        }
        
        Write-Host ""
        Write-Host "✓ Infrastructure deployment completed successfully!" -ForegroundColor Green
        Write-Host ""
        
        # Display outputs
        Write-Host "Infrastructure outputs:" -ForegroundColor Green
        Write-Host "-----------------------------------------------" -ForegroundColor Cyan
        $deployment.properties.outputs.PSObject.Properties | ForEach-Object {
            if ($_.Name -notlike '*Password*' -and $_.Name -notlike '*Key*') {
                Write-Host "$($_.Name):" -ForegroundColor Yellow -NoNewline
                Write-Host " $($_.Value.value)" -ForegroundColor White
            }
        }
        Write-Host "-----------------------------------------------" -ForegroundColor Cyan
        Write-Host ""
        
        # Save outputs to file
        $deployment.properties.outputs | ConvertTo-Json -Depth 10 | Out-File -FilePath $InfraOutputsFile -Encoding UTF8
        Write-Host "✓ Outputs saved to: $InfraOutputsFile" -ForegroundColor Green
        Write-Host ""
        
        return $deployment.properties.outputs
    }
}

function Build-PushImage {
    param($InfraOutputs)
    
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "  STAGE 2: Build and Push Docker Image" -ForegroundColor Cyan
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Build solution in Release configuration
    Write-Host "Building solution in Release configuration..." -ForegroundColor Yellow
    dotnet build "$RootPath\QuantConnect.Lean.sln" -c Release
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Solution build failed"
    }
    Write-Host "✓ Solution built successfully" -ForegroundColor Green
    Write-Host ""

    # Load infrastructure outputs if not provided
    if (-not $InfraOutputs) {
        if (-not (Test-Path $InfraOutputsFile)) {
            Write-Error "Infrastructure outputs file not found: $InfraOutputsFile. Please run infrastructure stage first."
        }
        $InfraOutputs = Get-Content $InfraOutputsFile | ConvertFrom-Json
    }
    
    $acrLoginServer = $InfraOutputs.containerRegistryLoginServer.value
    $acrUsername = $InfraOutputs.containerRegistryUsername.value
    $acrPassword = $InfraOutputs.containerRegistryPassword.value
    
    Write-Host "Container Registry: $acrLoginServer" -ForegroundColor Yellow
    Write-Host ""
  
    # Login to ACR
    Write-Host "Logging in to Azure Container Registry..." -ForegroundColor Yellow
    Write-Output $acrPassword | docker login $acrLoginServer --username $acrUsername --password-stdin
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to login to Azure Container Registry"
    }
    Write-Host "✓ Successfully logged in to ACR" -ForegroundColor Green
    Write-Host ""
    
    # Build image
    $imageName = "lean-custom"
    $fullImageName = "$acrLoginServer/${imageName}:$ImageTag"
    
    Write-Host "Building Docker image..." -ForegroundColor Yellow
    Write-Host "Image: $fullImageName" -ForegroundColor Yellow
    Write-Host ""
    
    docker build -t $fullImageName -f "$RootPath\DockerfileNew" $RootPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker build failed"
    }
    Write-Host ""
    Write-Host "✓ Image built successfully" -ForegroundColor Green
    Write-Host ""
    
    # Push image
    Write-Host "Pushing image to ACR..." -ForegroundColor Yellow
    docker push $fullImageName
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Docker push failed"
    }
    Write-Host ""
    Write-Host "✓ Image pushed successfully" -ForegroundColor Green
    Write-Host ""
}

function Deploy-Container {
    param($InfraOutputs)
    
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "  STAGE 3: Container Instance Deployment" -ForegroundColor Cyan
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Load infrastructure outputs if not provided
    if (-not $InfraOutputs) {
        if (-not (Test-Path $InfraOutputsFile)) {
            Write-Error "Infrastructure outputs file not found: $InfraOutputsFile. Please run infrastructure stage first."
        }
        $InfraOutputs = Get-Content $InfraOutputsFile | ConvertFrom-Json
    }
    
    # Verify container Bicep file exists
    if (-not (Test-Path $MainBicepContainer)) {
        Write-Error "Container Bicep file not found: $MainBicepContainer"
    }
    
    # Generate deployment name
    $DeploymentName = "lean-container-$Environment-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    
    Write-Host "Starting container deployment..." -ForegroundColor Yellow
    Write-Host "Deployment name: $DeploymentName" -ForegroundColor Yellow
    Write-Host "Image tag: $ImageTag" -ForegroundColor Yellow
    Write-Host ""
    
    # Build parameters in Azure format
    $containerParams = @{
        '$schema' = 'https://schema.management.azure.com/schemas/2018-05-01/subscriptionDeploymentParameters.json#'
        contentVersion = '1.0.0.0'
        parameters = @{
            environment = @{ value = $Environment }
            location = @{ value = $Location }
            imageTag = @{ value = $ImageTag }
            containerRegistryServer = @{ value = $InfraOutputs.containerRegistryLoginServer.value }
            containerRegistryUsername = @{ value = $InfraOutputs.containerRegistryUsername.value }
            containerRegistryPassword = @{ value = $InfraOutputs.containerRegistryPassword.value }
            storageAccountName = @{ value = $InfraOutputs.storageAccountName.value }
            fileShareName = @{ value = $InfraOutputs.fileShareName.value }
            logAnalyticsWorkspaceId = @{ value = $InfraOutputs.logAnalyticsWorkspaceId.value }
            logAnalyticsWorkspaceKey = @{ value = $InfraOutputs.logAnalyticsWorkspaceKey.value }
            keyVaultName = @{ value = $InfraOutputs.keyVaultName.value }
        }
    }
    
    # Convert to JSON for parameters
    $paramsJson = $containerParams | ConvertTo-Json -Depth 10
    $tempParamsFile = Join-Path $env:TEMP "container-params-$Environment.json"
    $paramsJson | Out-File -FilePath $tempParamsFile -Encoding UTF8
    
    if ($WhatIf) {
        Write-Host "Running validation (What-If mode)..." -ForegroundColor Yellow
        az deployment sub what-if `
            --name $DeploymentName `
            --location $Location `
            --template-file $MainBicepContainer `
            --parameters $tempParamsFile
    }
    else {
        $deployment = az deployment sub create `
            --name $DeploymentName `
            --location $Location `
            --template-file $MainBicepContainer `
            --parameters $tempParamsFile `
            --output json | ConvertFrom-Json
        
        # Clean up temp file
        Remove-Item $tempParamsFile -Force -ErrorAction SilentlyContinue
        
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Container deployment failed. Check the error messages above."
        }
        
        Write-Host ""
        Write-Host "✓ Container deployment completed successfully!" -ForegroundColor Green
        Write-Host ""
        
        # Display outputs
        Write-Host "Container outputs:" -ForegroundColor Green
        Write-Host "-----------------------------------------------" -ForegroundColor Cyan
        $deployment.properties.outputs.PSObject.Properties | ForEach-Object {
            Write-Host "$($_.Name):" -ForegroundColor Yellow -NoNewline
            Write-Host " $($_.Value.value)" -ForegroundColor White
        }
        Write-Host "-----------------------------------------------" -ForegroundColor Cyan
        Write-Host ""
        
        # Save outputs to file
        $deployment.properties.outputs | ConvertTo-Json -Depth 10 | Out-File -FilePath $ContainerOutputsFile -Encoding UTF8
        Write-Host "✓ Outputs saved to: $ContainerOutputsFile" -ForegroundColor Green
        Write-Host ""
        
        # Display next steps
        $resourceGroupName = $InfraOutputs.resourceGroupName.value
        $containerInstanceName = $deployment.properties.outputs.containerInstanceName.value
        
        Write-Host "Next steps:" -ForegroundColor Cyan
        Write-Host "1. Monitor container logs:" -ForegroundColor White
        Write-Host "   az container logs --resource-group $resourceGroupName --name $containerInstanceName --follow" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "2. View container in Azure Portal:" -ForegroundColor White
        Write-Host "   https://portal.azure.com/#@/resource/subscriptions/$SubscriptionId/resourceGroups/$resourceGroupName/providers/Microsoft.ContainerInstance/containerGroups/$containerInstanceName" -ForegroundColor Yellow
        Write-Host ""
    }
}

# ==============================================================================
# Main Execution
# ==============================================================================

try {
    Test-Prerequisites
    Connect-AzureAccount
    
    $infraOutputs = $null
    
    if ($Stage -eq 'infrastructure' -or $Stage -eq 'all') {
        $infraOutputs = Deploy-Infrastructure
        if ($WhatIf) {
            exit 0
        }
    }
    
    if ($Stage -eq 'image' -or $Stage -eq 'all') {
        Build-PushImage -InfraOutputs $infraOutputs
    }
    
    if ($Stage -eq 'container' -or $Stage -eq 'all') {
        if ($WhatIf) {
            Deploy-Container -InfraOutputs $infraOutputs
            exit 0
        }
        else {
            Deploy-Container -InfraOutputs $infraOutputs
        }
    }
    
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host "  Deployment Complete!" -ForegroundColor Cyan
    Write-Host "=============================================" -ForegroundColor Cyan
    Write-Host ""
}
catch {
    Write-Host ""
    Write-Host "=============================================" -ForegroundColor Red
    Write-Host "  Deployment Failed!" -ForegroundColor Red
    Write-Host "=============================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $_" -ForegroundColor Red
    Write-Host ""
    exit 1
}
