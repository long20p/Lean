# Lean Docker Deployment - .NET 9

This directory contains Docker configuration for running QuantConnect Lean in a containerized environment using .NET 9.

## Prerequisites

- Docker Desktop installed
- .NET 9 SDK installed
- Lean solution built successfully

## Quick Start

### 1. Build Your Algorithms

From the solution root:

```powershell
# Build in Debug mode
dotnet build -c Debug

# Or build in Release mode
dotnet build -c Release
```

### 2. Build Docker Image

**For Debug build:**
```powershell
cd C:\GitRepo\Finance\LeanFork
docker build -f Launcher/Dockerfile -t lean:net9-debug .
```

**For Release build:**
```powershell
docker build -f Launcher/Dockerfile --build-arg CONFIGURATION=Release -t lean:net9-latest .
```

### 3. Run the Container

**Basic run:**
```powershell
docker run --rm lean:net9-debug
```

**With volume mounts for Results:**
```powershell
docker run --rm -v "${PWD}/Results:/Results" lean:net9-debug
```

**With custom data directory:**
```powershell
docker run --rm `
  -v "${PWD}/Data:/Lean/Data" `
  -v "${PWD}/Results:/Results" `
  lean:net9-debug
```

**Interactive mode for debugging:**
```powershell
docker run --rm -it lean:net9-debug /bin/bash
```

## Configuration

### Environment Variables

You can override configuration using environment variables:

```powershell
docker run --rm `
  -e "algorithm-type-name=ExperimentalAlgorithm" `
  -e "environment=backtesting" `
  -v "${PWD}/Results:/Results" `
  lean:net9-debug
```

### Custom Config File

Mount a custom config.json:

```powershell
docker run --rm `
  -v "${PWD}/Launcher/custom-config.json:/Lean/config.json" `
  -v "${PWD}/Results:/Results" `
  lean:net9-debug
```

## Docker Compose (Optional)

Create a `docker-compose.yml` in the solution root:

```yaml
version: '3.8'

services:
  lean:
    build:
  context: .
      dockerfile: Launcher/Dockerfile
      args:
  CONFIGURATION: Debug
        FRAMEWORK: net9.0
    volumes:
      - ./Data:/Lean/Data:ro
      - ./Results:/Results
    environment:
      - algorithm-type-name=ExperimentalAlgorithm
      - environment=backtesting
```

Run with:
```powershell
docker-compose up --build
```

## Troubleshooting

### Check if Lean Launcher is present

```powershell
docker run --rm lean:net9-debug ls -la /Lean/
```

### Verify algorithm DLL

```powershell
docker run --rm lean:net9-debug ls -la /Lean/Algorithm/
```

### Check .NET runtime version

```powershell
docker run --rm lean:net9-debug dotnet --version
```

### View container logs

```powershell
docker logs <container-id>
```

### Debug inside container

```powershell
docker run --rm -it --entrypoint /bin/bash lean:net9-debug
```

## Advanced Usage

### Multi-Stage Build (Alternative)

For a self-contained build that doesn't require pre-built binaries, create a multi-stage Dockerfile:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet publish Launcher/QuantConnect.Lean.Launcher.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /Lean
COPY --from=build /app .
ENTRYPOINT ["dotnet", "QuantConnect.Lean.Launcher.dll"]
```

### Pushing to Registry

```powershell
# Tag for registry
docker tag lean:net9-latest your-registry.com/lean:net9-latest

# Push to registry
docker push your-registry.com/lean:net9-latest
```

## Performance Optimization

### Use Release Build
Always use Release configuration for production:
```powershell
docker build -f Launcher/Dockerfile --build-arg CONFIGURATION=Release -t lean:net9-latest .
```

### Limit Memory
```powershell
docker run --rm --memory=4g --memory-swap=4g lean:net9-debug
```

### Limit CPU
```powershell
docker run --rm --cpus=2 lean:net9-debug
```

## File Structure

```
Launcher/
├── Dockerfile        # Main Docker configuration
├── .dockerignore          # Files to exclude from build context
├── DOCKER_README.md    # This file
└── config.json      # Lean configuration file
```

## Support

For issues related to:
- **Docker setup**: Check this README
- **Lean configuration**: See `config.json` documentation
- **Algorithm errors**: Check your algorithm implementation
- **Build errors**: Ensure .NET 9 SDK is properly installed

## License

This Docker configuration is part of the QuantConnect Lean project.
See the main repository LICENSE file for details.
