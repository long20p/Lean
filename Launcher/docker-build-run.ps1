# ======================================================================
# Lean Docker Build and Run Script for PowerShell
# ======================================================================
# This script builds and runs the Lean Docker container with .NET 9
#
# Usage:
#   .\docker-build-run.ps1 -Configuration Debug
#   .\docker-build-run.ps1 -Configuration Release -Run
# ======================================================================

param(
    [Parameter(Mandatory=$false)]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [Parameter(Mandatory=$false)]
    [switch]$Run,
    
    [Parameter(Mandatory=$false)]
    [switch]$Interactive,
    
    [Parameter(Mandatory=$false)]
    [string]$AlgorithmName = ""
)

$ErrorActionPreference = "Stop"

# Set working directory to solution root
$SolutionRoot = Split-Path -Parent $PSScriptRoot
Set-Location $SolutionRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Lean .NET 9 Docker Build & Run Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Build the .NET solution
Write-Host "Step 1: Building .NET solution ($Configuration)..." -ForegroundColor Yellow
try {
    dotnet build -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed with exit code $LASTEXITCODE"
    }
    Write-Host "✓ Build successful" -ForegroundColor Green
} catch {
    Write-Host "✗ Build failed: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Step 2: Build Docker image
$ImageTag = if ($Configuration -eq "Release") { "lean:net9-latest" } else { "lean:net9-debug" }

Write-Host "Step 2: Building Docker image ($ImageTag)..." -ForegroundColor Yellow
try {
    $BuildArgs = @(
        "build",
        "-f", "Launcher/Dockerfile",
        "--build-arg", "CONFIGURATION=$Configuration",
     "-t", $ImageTag,
        "."
    )
    
    & docker @BuildArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Docker build failed with exit code $LASTEXITCODE"
    }
    Write-Host "✓ Docker image built successfully" -ForegroundColor Green
} catch {
    Write-Host "✗ Docker build failed: $_" -ForegroundColor Red
 exit 1
}

Write-Host ""

# Step 3: Run container (if requested)
if ($Run) {
    Write-Host "Step 3: Running Docker container..." -ForegroundColor Yellow
    
    $ResultsPath = Join-Path $SolutionRoot "Results"
    
    # Create Results directory if it doesn't exist
    if (!(Test-Path $ResultsPath)) {
  New-Item -ItemType Directory -Path $ResultsPath | Out-Null
    }
    
    $RunArgs = @(
        "run",
        "--rm"
    )
    
    # Add volume mounts
    $RunArgs += "-v"
    $RunArgs += "${ResultsPath}:/Results"
    
    # Add algorithm name if specified
    if ($AlgorithmName) {
      $RunArgs += "-e"
        $RunArgs += "algorithm-type-name=$AlgorithmName"
    }
    
    # Interactive mode
    if ($Interactive) {
        $RunArgs += "-it"
        $RunArgs += "--entrypoint"
   $RunArgs += "/bin/bash"
    }
 
    $RunArgs += $ImageTag
    
    Write-Host "Running: docker $($RunArgs -join ' ')" -ForegroundColor Gray
    Write-Host ""
    
    & docker @RunArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "✓ Container executed successfully" -ForegroundColor Green
        Write-Host "Results saved to: $ResultsPath" -ForegroundColor Cyan
    } else {
 Write-Host ""
     Write-Host "✗ Container execution failed" -ForegroundColor Red
        exit 1
    }
} else {
 Write-Host "Docker image built. To run it:" -ForegroundColor Cyan
    Write-Host "  docker run --rm -v `"`${PWD}/Results:/Results`" $ImageTag" -ForegroundColor White
    Write-Host ""
    Write-Host "Or use this script with -Run flag:" -ForegroundColor Cyan
    Write-Host "  .\Launcher\docker-build-run.ps1 -Configuration $Configuration -Run" -ForegroundColor White
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Done!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
