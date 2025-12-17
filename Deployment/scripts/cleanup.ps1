<#
.SYNOPSIS
    Removes all Azure resources for the Lean Trading Engine deployment.

.DESCRIPTION
    This script deletes the resource group and all contained resources for
    the specified environment.

.PARAMETER Environment
    The environment to clean up (dev, staging, prod).

.PARAMETER Location
    The Azure region (default: eastus).

.PARAMETER Force
    Skip confirmation prompt.

.EXAMPLE
    .\cleanup.ps1 -Environment dev
    Removes all dev environment resources after confirmation.

.EXAMPLE
  .\cleanup.ps1 -Environment staging -Force
    Removes all staging resources without confirmation.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment,

    [Parameter(Mandatory = $false)]
    [string]$Location = 'eastus',

    [Parameter(Mandatory = $false)]
    [switch]$Force
)

# Set error action preference
$ErrorActionPreference = 'Stop'

# Determine location short name
$locationShort = switch ($Location) {
    'eastus' { 'eus' }
    'westus' { 'wus' }
    'eastus2' { 'eus2' }
    'westus2' { 'wus2' }
    default { $Location }
}

$resourceGroupName = "rg-lp-lean-$Environment-$locationShort"

# Banner
Write-Host "=============================================" -ForegroundColor Red
Write-Host "  Lean Trading Engine - Cleanup" -ForegroundColor Red
Write-Host "=============================================" -ForegroundColor Red
Write-Host ""
Write-Host "Environment:     $Environment" -ForegroundColor Yellow
Write-Host "Location:        $Location" -ForegroundColor Yellow
Write-Host "Resource Group:  $resourceGroupName" -ForegroundColor Yellow
Write-Host ""

# Warning
Write-Host "WARNING: This will delete all resources in the resource group!" -ForegroundColor Red
Write-Host ""

# Check if resource group exists
Write-Host "Checking if resource group exists..." -ForegroundColor Green
$rgExists = az group exists --name $resourceGroupName
if ($rgExists -eq 'false') {
    Write-Host "✓ Resource group does not exist: $resourceGroupName" -ForegroundColor Green
    Write-Host "Nothing to clean up." -ForegroundColor Yellow
    exit 0
}

# List resources
Write-Host "Resources to be deleted:" -ForegroundColor Yellow
Write-Host "-----------------------------------------------" -ForegroundColor Cyan
$resources = az resource list --resource-group $resourceGroupName --output json | ConvertFrom-Json
foreach ($resource in $resources) {
    Write-Host "  - $($resource.type): $($resource.name)" -ForegroundColor White
}
Write-Host "-----------------------------------------------" -ForegroundColor Cyan
Write-Host ""

# Confirmation
if (-not $Force) {
    $confirmation = Read-Host "Are you sure you want to delete these resources? Type 'DELETE' to confirm"
    if ($confirmation -ne 'DELETE') {
   Write-Host "Cleanup cancelled." -ForegroundColor Yellow
      exit 0
    }
}

# Delete resource group
Write-Host ""
Write-Host "Deleting resource group: $resourceGroupName" -ForegroundColor Red
Write-Host "This may take several minutes..." -ForegroundColor Yellow
Write-Host ""

az group delete --name $resourceGroupName --yes --no-wait

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to delete resource group"
}

Write-Host "✓ Resource group deletion initiated" -ForegroundColor Green
Write-Host ""
Write-Host "Note: Deletion is running in the background." -ForegroundColor Yellow
Write-Host "You can check the status with:" -ForegroundColor Yellow
Write-Host "  az group show --name $resourceGroupName" -ForegroundColor Cyan
Write-Host ""

# Delete deployment outputs file
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$DeploymentPath = Split-Path -Parent $ScriptPath
$OutputsFile = Join-Path $DeploymentPath "outputs\$Environment-outputs.json"

if (Test-Path $OutputsFile) {
Remove-Item $OutputsFile -Force
    Write-Host "✓ Deleted outputs file: $OutputsFile" -ForegroundColor Green
}

Write-Host ""
Write-Host "=============================================" -ForegroundColor Red
Write-Host "  Cleanup Initiated!" -ForegroundColor Red
Write-Host "=============================================" -ForegroundColor Red
