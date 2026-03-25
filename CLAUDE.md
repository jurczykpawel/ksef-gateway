# ksef-gateway

Universal REST API gateway wrapping the official CIRFMF KSeF C# SDK.

## Stack
- **ksef-api**: ASP.NET 8 Minimal API + CIRFMF KSeF.Client SDK
- **ksef-pdf**: Node.js + CIRFMF ksef-pdf-generator
- **Orchestration**: docker-compose

## Quick start
```bash
cp .env.example .env
# Edit .env with your KSEF_TOKEN, KSEF_NIP, GITHUB_PAT
docker-compose up --build
# API: http://localhost:8080
# Swagger: http://localhost:8080/swagger
```

## Architecture
- `SdkReflector` discovers all methods on `IKSeFClient` (13 sub-interfaces) via reflection
- `EndpointMapper` auto-registers each method as `POST /ksef/{group}/{method}`
- `TokenManager` handles KSeF token auth + auto-refresh
- Encryption is transparent: callers send plaintext XML, gateway encrypts

## Key files
- `src/KSeFGateway.Api/Discovery/SdkReflector.cs` - reflection engine
- `src/KSeFGateway.Api/Discovery/EndpointMapper.cs` - route registration
- `src/KSeFGateway.Api/Auth/TokenManager.cs` - auth lifecycle
- `src/KSeFGateway.Api/Endpoints/WorkflowEndpoints.cs` - convenience endpoints

## SDK
CIRFMF SDK is on GitHub Packages (not public nuget.org). Requires GITHUB_PAT with `read:packages`.

## Build without local .NET
Everything builds inside Docker. No local .NET SDK required.
```bash
docker-compose build
```

## Tests
```bash
# Inside Docker
docker-compose run --rm ksef-api dotnet test
```
