# ksef-gateway

Universal REST API gateway wrapping the official CIRFMF KSeF C# SDK.

## Stack
- **ksef-api**: ASP.NET 9 Minimal API + CIRFMF KSeF.Client SDK (NuGet, GitHub Packages)
- **ksef-pdf**: Node.js + tsx + CIRFMF ksef-pdf-generator (git submodule)
- **Orchestration**: Docker Compose (2 containers, no DB, no Redis)

## Quick start
```bash
cp .env.example .env  # set GITHUB_PAT
docker compose --profile tools run --rm token-generator  # generates KSEF_TOKEN + KSEF_NIP
# paste output into .env
docker compose up --build
# API: http://localhost:8080
# Docs: http://localhost:8080/scalar/v1
```

## Key files
- `src/KSeFGateway.Api/Discovery/SdkReflector.cs` - reflection engine (discovers 60+ SDK methods)
- `src/KSeFGateway.Api/Discovery/EndpointMapper.cs` - auto-registers POST /ksef/{group}/{method}
- `src/KSeFGateway.Api/Auth/TokenManager.cs` - KSeF token auth + background refresh
- `src/KSeFGateway.Api/Endpoints/WorkflowEndpoints.cs` - high-level: /ksef/send, /ksef/send/json, /ksef/invoice/{nr}/pdf
- `src/KSeFGateway.Api/Endpoints/HealthEndpoints.cs` - /health, /ksef/status
- `src/KSeFGateway.Api/Middleware/ErrorHandlingMiddleware.cs` - KsefApiException → HTTP responses
- `pdf-service/src/server.ts` - PDF generation, JSON→XML conversion, QR codes
- `pdf-service/lib/` - git submodule: CIRFMF/ksef-pdf-generator
- `tools/TokenGenerator/` - one-command KSeF TEST token generator
- `tools/InvoiceDemo/` - E2E invoice send demo

## SDK
CIRFMF SDK on GitHub Packages (not public nuget.org). Requires GITHUB_PAT with `read:packages`.
`nuget.config` in `src/KSeFGateway.Api/` and `tools/*/` configures the source.

## Build
Everything builds inside Docker. No local .NET SDK required.
```bash
docker compose build                                    # all services
docker compose --profile tools build token-generator    # tools
```

## Tests
```bash
# .NET unit tests inside Docker (no local SDK required)
docker compose run --rm ksef-api-tests
# PDF service (local)
cd pdf-service && npm test
```
Note: `ksef-api`'s runtime image has no SDK and a fixed `ENTRYPOINT`, so it can't run `dotnet test` directly — `ksef-api-tests` is a separate compose service built from the `test` stage in `KSeFGateway.Api/Dockerfile` (profile `tools`, mirrors CI's `build-api` job, excludes `Category=Integration`).

## Architecture
- `SdkReflector` discovers all methods on `IKSeFClient` (13 sub-interfaces) via .NET reflection at startup
- `EndpointMapper` registers each method as `POST /ksef/{group}/{method}` with dynamic JSON→SDK parameter mapping
- `TokenManager` (BackgroundService) authenticates via `IAuthCoordinator.AuthKsefTokenAsync()` and auto-refreshes at 80% TTL
- `WorkflowEndpoints` provide high-level flows: XML/JSON → encrypt → session → send → KSeF number
- `pdf-service` uses xml-js for bidirectional JSON/XML conversion and CIRFMF lib for PDF rendering
- QR codes use SHA-256 hash of KSeF-canonical XML + P_1 date + seller NIP

## Git workflow
- **main**: stable releases only, tagged (v0.1.0, ...)
- **dev**: active development, feature branches merge here
- Feature branches: `feat/feature-name`, `fix/bug-name`
- Merge: `--no-ff` with descriptive commit message
- Never commit directly to main
